import Foundation
import SwiftUI
import MLX
import MLXLLM
import MLXLMCommon
import HuggingFace
import Tokenizers

// MARK: - MLXLMCommon Protocol Bridges

/// Bridges HuggingFace Hub's download API to MLXLMCommon.Downloader protocol.
/// This avoids depending on MLXHuggingFace (which requires Swift Macro compilation
/// and breaks XcodeGen-based CI builds).
struct HubDownloaderBridge: MLXLMCommon.Downloader {

    func download(
        id: String,
        revision: String?,
        matching patterns: [String],
        useLatest: Bool,
        progressHandler: @Sendable @escaping (Foundation.Progress) -> Void
    ) async throws -> URL {
        return try await HubClient.default.downloadSnapshot(
            of: id,
            revision: revision,
            matching: patterns
        ) { progress in
            progressHandler(progress)
        }
    }
}

/// Bridges swift-transformers' Tokenizer to MLXLMCommon.Tokenizer protocol.
struct TokenizerBridge: MLXLMCommon.Tokenizer {
    private let upstream: any Tokenizers.Tokenizer

    init(_ upstream: any Tokenizers.Tokenizer) {
        self.upstream = upstream
    }

    func encode(text: String, addSpecialTokens: Bool) -> [Int] {
        upstream.encode(text: text, addSpecialTokens: addSpecialTokens)
    }

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

/// Loads a tokenizer from a local directory using swift-transformers' AutoTokenizer,
/// then wraps it in our TokenizerBridge.
struct AutoTokenizerLoader: MLXLMCommon.TokenizerLoader {
    func load(from directory: URL) async throws -> any MLXLMCommon.Tokenizer {
        let upstream = try await Tokenizers.AutoTokenizer.from(modelFolder: directory)
        return TokenizerBridge(upstream)
    }
}

// MARK: - LLM Service

@MainActor
class LLMService: ObservableObject {
    @Published var summaryText = ""
    @Published var isProcessing = false
    @Published var loadingProgress: String = ""

    private var modelContainer: ModelContainer?
    private let modelConfiguration = ModelConfiguration(id: "mlx-community/Llama-3.2-1B-Instruct-4bit")

    // Shared bridges (created once, reused across calls)
    private let downloader = HubDownloaderBridge()
    private let tokenizerLoader = AutoTokenizerLoader()

    // MARK: - Model Loading

    private func ensureModelLoaded() async throws -> ModelContainer {
        if let container = modelContainer { return container }

        loadingProgress = String(localized: "Loading model...")

        let container = try await LLMModelFactory.shared.loadContainer(
            from: downloader,
            using: tokenizerLoader,
            configuration: modelConfiguration
        ) { progress in
            // Progress callback for model download
        }

        modelContainer = container
        return container
    }

    // MARK: - Text Generation (Core)

    private func generate(systemPrompt: String, userContent: String, temperature: Float = 0.6) async throws -> String {
        let container = try await ensureModelLoaded()

        let fullPrompt = """
            <|begin_of_text|><|start_header_id|>system<|end_header_id|>

            \(systemPrompt)<|eot_id|><|start_header_id|>user<|end_header_id|>

            \(userContent)<|eot_id|><|start_header_id|>assistant<|end_header_id|>

            """

        var generatedText = ""

        let _ = try await container.perform { model, tokenizer in
            let promptTokens = try tokenizer.encode(text: fullPrompt)

            let _ = try MLXLMCommon.generate(
                promptTokens: promptTokens,
                parameters: GenerateParameters(temperature: temperature),
                model: model,
                tokenizer: tokenizer,
                extraEOSTokens: Set<String>(),
                didGenerate: { tokens in
                    if let newText = tokenizer.decode(tokenIds: tokens) {
                        generatedText = newText
                    }
                    return .more
                }
            )
        }

        return generatedText
    }

    // MARK: - Public API: Summary

    func generateSummary(text: String, context: MeetingContext) async {
        guard !text.isEmpty else { return }

        isProcessing = true
        summaryText = ""
        loadingProgress = String(localized: "Loading model...")

        do {
            let container = try await ensureModelLoaded()

            loadingProgress = String(localized: "Generating summary...")

            let systemPrompt = """
                You are an expert AI meeting summarizer. \
                The transcript lacks speaker tags. Try to deduce different speakers from the context \
                (e.g. Speaker A, Speaker B) and reconstruct the dialogue. \
                Provide a concise, well-structured summary of who said what, key points, and action items. \
                Context of the meeting: \(context.rawValue).
                """

            let fullPrompt = "<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n\(systemPrompt)<|eot_id|><|start_header_id|>user<|end_header_id|>\n\nTranscript:\n\(text)<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n"

            let _ = try await container.perform { model, tokenizer in
                let promptTokens = try tokenizer.encode(text: fullPrompt)

                let _ = try MLXLMCommon.generate(
                    promptTokens: promptTokens,
                    parameters: GenerateParameters(temperature: 0.6),
                    model: model,
                    tokenizer: tokenizer,
                    extraEOSTokens: Set<String>(),
                    didGenerate: { tokens in
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
            let formatString = String(localized: "MLX Error: %@")
            summaryText = String(format: formatString, error.localizedDescription)
        }

        isProcessing = false
        loadingProgress = ""
    }

    // MARK: - Public API: Speaker Diarization

    func formatTranscriptWithSpeakers(text: String) async -> String {
        guard !text.isEmpty else { return "" }

        isProcessing = true
        loadingProgress = String(localized: "Identifying speakers...")

        do {
            let systemPrompt = """
                You are a transcription formatting assistant. \
                Rewrite the following raw audio transcription by assigning Speaker tags \
                (e.g., Speaker A, Speaker B) based on conversational context, turn-taking, and tone. \
                DO NOT summarize the text. DO NOT omit any spoken words. \
                Preserve the original dialogue exactly as spoken, but add speaker labels \
                at the beginning of each conversational turn. \
                Output ONLY the formatted transcript.
                """

            let result = try await generate(
                systemPrompt: systemPrompt,
                userContent: "Transcript:\n\(text)",
                temperature: 0.1
            )

            isProcessing = false
            loadingProgress = ""
            return result.isEmpty ? text : result

        } catch {
            isProcessing = false
            loadingProgress = ""
            return text  // Fallback to raw transcript on error
        }
    }
}
