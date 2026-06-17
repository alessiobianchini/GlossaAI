import Foundation
import SwiftUI
import MLX
import MLXLLM
import MLXLMCommon
import HuggingFace
import Tokenizers

// MARK: - MLXLMCommon Protocol Bridges

/// Bridges swift-huggingface's HubClient to MLXLMCommon.Downloader.
struct HubDownloaderBridge: MLXLMCommon.Downloader {
    func download(
        id: String,
        revision: String?,
        matching patterns: [String],
        useLatest: Bool,
        progressHandler: @Sendable @escaping (Foundation.Progress) -> Void
    ) async throws -> URL {
        let parts = id.split(separator: "/", maxSplits: 1)
        let repoId: Repo.ID
        if parts.count == 2 {
            repoId = Repo.ID(namespace: String(parts[0]), name: String(parts[1]))
        } else {
            repoId = Repo.ID(namespace: "mlx-community", name: id)
        }
        
        return try await HubClient.default.downloadSnapshot(
            of: repoId,
            revision: revision ?? "main",
            matching: patterns,
            progressHandler: progressHandler
        )
    }
}

/// Bridges swift-transformers' Tokenizer to MLXLMCommon.Tokenizer.
final class TokenizerBridge: MLXLMCommon.Tokenizer, @unchecked Sendable {
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
        let anyMessages = messages.map { $0 as [String: Any] }
        let anyTools = tools?.map { $0 as [String: Any] }
        do {
            return try upstream.applyChatTemplate(
                messages: anyMessages,
                tools: anyTools,
                additionalContext: additionalContext
            )
        } catch Tokenizers.TokenizerError.missingChatTemplate {
            throw MLXLMCommon.TokenizerError.missingChatTemplate
        }
    }
}

/// Loads a tokenizer from a local directory and wraps it in TokenizerBridge.
struct AutoTokenizerLoader: MLXLMCommon.TokenizerLoader {
    func load(from directory: URL) async throws -> any MLXLMCommon.Tokenizer {
        let upstream = try await Tokenizers.AutoTokenizer.from(modelFolder: directory)
        return TokenizerBridge(upstream)
    }
}

// MARK: - Output Accumulator for Sendable closures
final class OutputAccumulator: @unchecked Sendable {
    var text: String = ""
}

// MARK: - LLM Service

@MainActor
class LLMService: ObservableObject {
    @Published var summaryText = ""
    @Published var isProcessing = false
    @Published var loadingProgress: String = ""

    private var modelContainer: ModelContainer?
    private let modelConfiguration = ModelConfiguration(id: "mlx-community/Llama-3.2-1B-Instruct-4bit")
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
        ) { _ in }
        modelContainer = container
        return container
    }

    // MARK: - Text Generation

    private func generate(
        systemPrompt: String,
        userContent: String,
        temperature: Float = 0.6
    ) async throws -> String {
        let container = try await ensureModelLoaded()

        let messages: [[String: String]] = [
            ["role": "system", "content": systemPrompt],
            ["role": "user", "content": userContent]
        ]

        let accumulator = OutputAccumulator()

        try await container.perform { (context: ModelContext) in
            let promptTokens = try context.tokenizer.applyChatTemplate(
                messages: messages,
                tools: nil,
                additionalContext: nil
            )

            let input = LMInput(tokens: MLXArray(promptTokens))
            let result = try MLXLMCommon.generate(
                input: input,
                parameters: GenerateParameters(temperature: temperature),
                context: context
            ) { newTokens in
                accumulator.text += context.tokenizer.decode(tokenIds: newTokens)
                return .more
            }
            MLX.eval(result.output)
        }

        return accumulator.text
    }

    // MARK: - Public API: Summary

    func generateSummary(text: String, context: MeetingContext) async {
        guard !text.isEmpty else { return }
        isProcessing = true
        summaryText = ""
        loadingProgress = String(localized: "Loading model...")

        do {
            _ = try await ensureModelLoaded()
            loadingProgress = String(localized: "Generating summary...")

            let systemPrompt = """
                You are an expert AI meeting summarizer. \
                The transcript lacks speaker tags. Try to deduce different speakers from the context \
                (e.g. Speaker A, Speaker B) and reconstruct the dialogue. \
                Provide a concise, well-structured summary of who said what, key points, and action items. \
                Context of the meeting: \(context.rawValue).
                """

            summaryText = try await generate(
                systemPrompt: systemPrompt,
                userContent: "Transcript:\n\(text)"
            )
        } catch {
            summaryText = String(format: String(localized: "MLX Error: %@"), error.localizedDescription)
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
            return text
        }
    }
}
