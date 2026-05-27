import Foundation
import SwiftUI
import MLX
import MLXRandom
import MLXLLM

class LLMService: ObservableObject {
    @Published var summaryText = ""
    @Published var isProcessing = false
    @Published var loadingProgress: String = ""
    
    // Variabili di stato per mantenere in memoria il modello
    private var modelContext: ModelContext?
    
    func generateSummary(text: String, context: MeetingContext) async {
        guard !text.isEmpty else { return }
        
        DispatchQueue.main.async {
            self.isProcessing = true
            self.summaryText = ""
            self.loadingProgress = "Caricamento modello MLX in corso..."
        }
        
        do {
             MLX.Device.setDevice(.gpu) // Usa Neural Engine/GPU
             
             // 1. Caricamento Modello
             if modelContext == nil {
                 let modelConfig = "mlx-community/Phi-3-mini-4k-instruct-4bit"
                 let modelFactory = ModelFactory()
                 let loadedModel = try await modelFactory.load(id: modelConfig) { progress in
                     DispatchQueue.main.async {
                         self.loadingProgress = String(format: "Download pesi: %.0f%%", progress.fractionCompleted * 100)
                     }
                 }
                 self.modelContext = loadedModel
             }
             
             DispatchQueue.main.async {
                 self.loadingProgress = "Generazione in corso..."
             }
             
             // 2. Preparazione Prompt
             let systemPrompt = "Sei un assistente specializzato in contesti: \(context.rawValue). Scrivi un riassunto dettagliato del seguente testo."
             let fullPrompt = "<|system|>\n\(systemPrompt)<|end|>\n<|user|>\n\(text)<|end|>\n<|assistant|>\n"
             
             // 3. Generazione in Streaming
             if let modelContext = modelContext {
                 let stream = try await modelContext.model.generate(
                    prompt: fullPrompt,
                    tokenizer: modelContext.tokenizer,
                    maxTokens: 500
                 )
                 
                 for try await token in stream {
                     DispatchQueue.main.async {
                         self.summaryText += token.text
                     }
                 }
             }
             
        } catch {
            DispatchQueue.main.async {
                self.summaryText = "Errore MLX: \(error.localizedDescription)"
            }
        }
        
        DispatchQueue.main.async {
            self.isProcessing = false
            self.loadingProgress = ""
        }
    }
}
