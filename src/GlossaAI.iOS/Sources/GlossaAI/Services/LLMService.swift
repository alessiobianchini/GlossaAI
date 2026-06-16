import Foundation
import SwiftUI
import MLX
import MLXLLM
import MLXLMCommon
import MLXRandom

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
                    from: #hubDownloader(),
                    using: #huggingFaceTokenizerLoader(),
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
                
                let _ = try MLXLLM.generate(
                    promptTokens: promptTokens,
                    parameters: GenerateParameters(temperature: 0.6),
                    model: model,
                    tokenizer: tokenizer,
                    extraTokens: Set()
                ) { tokens in
                    // Decode incrementally
                    if let newText = tokenizer.decode(tokens: tokens) {
                        Task { @MainActor in
                            self.summaryText = newText
                        }
                    }
                    return .more
                }
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
