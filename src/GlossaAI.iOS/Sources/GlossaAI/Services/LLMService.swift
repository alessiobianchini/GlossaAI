import Foundation
import SwiftUI
import MLX
import MLXLLM
import MLXLMCommon
import HuggingFace
import Tokenizers

struct HubBridge: MLXLMCommon.Downloader {
    private let upstream: HuggingFace.HubClient

    init(_ upstream: HuggingFace.HubClient) {
        self.upstream = upstream
    }

    public func download(
        id: String,
        revision: String?,
        matching patterns: [String],
        useLatest: Bool,
        progressHandler: @Sendable @escaping (Foundation.Progress) -> Void
    ) async throws -> URL {                        
        guard let repoID = HuggingFace.Repo.ID(rawValue: id) else {
            throw NSError(domain: "LLMService", code: 2, userInfo: [NSLocalizedDescriptionKey: "Invalid Hugging Face repo ID: \(id)"])
        }
        let revision = revision ?? "main"

        return try await upstream.downloadSnapshot(
            of: repoID,
            revision: revision,
            matching: patterns,
            progressHandler: { @MainActor progress in
                progressHandler(progress)
            }
        )
    }                    
}

struct TokenizerBridge: MLXLMCommon.Tokenizer {
    private let upstream: any Tokenizers.Tokenizer

    init(_ upstream: any Tokenizers.Tokenizer) {
        self.upstream = upstream
    }

    func encode(text: String, addSpecialTokens: Bool) -> [Int] {
        upstream.encode(text: text, addSpecialTokens: addSpecialTokens)
    }

    // swift-transformers uses `decode(tokens:)` instead of `decode(tokenIds:)`.
    func decode(tokenIds: [Int], skipSpecialTokens: Bool) -> String {
        upstream.decode(tokens: tokenIds, skipSpecialTokens: skipSpecialTokens)
    }

    func convertTokenToId(_ token: String) -> Int? {
        upstream.convertTokenToId(token)
    }

    func convertIdToToken(_ id: Int) -> String? {
        upstream.convertIdToToken(id)
    }

    var bosToken: String? { upstream.bosToken }
    var eosToken: String? { upstream.eosToken }
    var unknownToken: String? { upstream.unknownToken }

    func applyChatTemplate(
        messages: [[String: any Sendable]],
        tools: [[String: any Sendable]]?,
        additionalContext: [String: any Sendable]?
    ) throws -> [Int] {
        do {
            return try upstream.applyChatTemplate(
                messages: messages, tools: tools, additionalContext: additionalContext)
        } catch Tokenizers.TokenizerError.missingChatTemplate {
            throw MLXLMCommon.TokenizerError.missingChatTemplate
        } catch {
            throw error
        }
    }
}

struct TransformersLoader: MLXLMCommon.TokenizerLoader {
    public init() {}

    public func load(from directory: URL) async throws -> any MLXLMCommon.Tokenizer {
        let upstream = try await Tokenizers.AutoTokenizer.from(modelFolder: directory)
        return TokenizerBridge(upstream)
    }
}

class LLMService: ObservableObject {
    @Published var summaryText = ""
    @Published var isProcessing = false
    @Published var loadingProgress: String = ""
    
    // MLX 3.0 Model Container
    private var modelContainer: ModelContainer?
    
    // We choose Llama-3.2-1B-Instruct-4bit as it's lightweight for iOS
    private let modelConfiguration = ModelConfiguration(id: "mlx-community/Llama-3.2-1B-Instruct-4bit")
    
    func generateSummary(text: String, context: MeetingContext) async {
        guard !text.isEmpty else { return }
        
        await MainActor.run {
            self.isProcessing = true
            self.summaryText = ""
            self.loadingProgress = String(localized: "Loading model...")
        }
        
        do {
            // Load container only once
            if modelContainer == nil {
                modelContainer = try await LLMModelFactory.shared.loadContainer(
                    from: HubBridge(HubClient()),
                    using: TransformersLoader(),
                    configuration: modelConfiguration
                ) { progress in
                    // We could update progress here if we wanted
                }
            }
            
            guard let container = modelContainer else {
                throw NSError(domain: "LLMService", code: 1, userInfo: [NSLocalizedDescriptionKey: "Model container not initialized"])
            }
            
            await MainActor.run {
                self.loadingProgress = String(localized: "Generating summary...")
            }
            
            // Format prompt
            let systemPrompt = "You are an expert AI meeting summarizer. The transcript lacks speaker tags. Try to deduce different speakers from the context (e.g. Speaker A, Speaker B) and reconstruct the dialogue. Provide a concise, well-structured summary of who said what, key points, and action items. Context of the meeting: \(context.rawValue)."
            
            // Llama 3 Instruct Format
            let fullPrompt = "<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n\(systemPrompt)<|eot_id|><|start_header_id|>user<|end_header_id|>\n\nTranscript:\n\(text)<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n"
            
            // Generate tokens
            let _ = try await container.perform { model, tokenizer in
                let promptTokens = try tokenizer.encode(text: fullPrompt)
                
                let _ = try MLXLMCommon.generate(
                    promptTokens: promptTokens,
                    parameters: GenerateParameters(temperature: 0.6),
                    model: model,
                    tokenizer: tokenizer,
                    extraEOSTokens: Set<String>(),
                    didGenerate: { tokens in
                        // Decode incrementally
                        if let newText = tokenizer.decode(tokenIds: tokens) {
                            Task { @MainActor in
                                self.summaryText = newText
                            }
                        }
                        return .more
                    }
                )
            }
            
        } catch {
            await MainActor.run {
                let formatString = String(localized: "MLX Error: %@")
                self.summaryText = String(format: formatString, error.localizedDescription)
            }
        }
        
        await MainActor.run {
            self.isProcessing = false
            self.loadingProgress = ""
        }
    }
}
