import SwiftUI

struct ContentView: View {
    @StateObject private var speechRecognizer = SpeechRecognizer()
    @StateObject private var llmService = LLMService()
    
    @State private var selectedContext: MeetingContext = .general
    
    // Branding Colors
    let brandBackground = Color(red: 0.05, green: 0.05, blue: 0.08)
    let brandAccent = Color(red: 0.45, green: 0.2, blue: 0.9) // Neon Purple
    let brandSecondary = Color(red: 0.1, green: 0.6, blue: 0.9) // Neon Blue
    
    var body: some View {
        NavigationView {
            ZStack {
                // Background Gradient
                LinearGradient(gradient: Gradient(colors: [brandBackground, Color.black]),
                               startPoint: .topLeading, endPoint: .bottomTrailing)
                    .edgesIgnoringSafeArea(.all)
                
                VStack(spacing: 24) {
                    // Selezione Contesto
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Meeting Context")
                            .font(.subheadline)
                            .fontWeight(.semibold)
                            .foregroundColor(.gray)
                            .padding(.horizontal)
                        
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 12) {
                                ForEach(MeetingContext.allCases) { context in
                                    Button(action: {
                                        withAnimation(.spring()) {
                                            selectedContext = context
                                        }
                                    }) {
                                        Text(context.rawValue)
                                            .font(.system(size: 14, weight: .medium, design: .rounded))
                                            .padding(.horizontal, 16)
                                            .padding(.vertical, 10)
                                            .background(
                                                selectedContext == context ?
                                                LinearGradient(gradient: Gradient(colors: [brandAccent, brandSecondary]), startPoint: .leading, endPoint: .trailing) :
                                                    LinearGradient(gradient: Gradient(colors: [Color.white.opacity(0.1), Color.white.opacity(0.05)]), startPoint: .leading, endPoint: .trailing)
                                            )
                                            .foregroundColor(selectedContext == context ? .white : .gray)
                                            .cornerRadius(20)
                                            .overlay(
                                                RoundedRectangle(cornerRadius: 20)
                                                    .stroke(Color.white.opacity(0.1), lineWidth: 1)
                                            )
                                    }
                                }
                            }
                            .padding(.horizontal)
                        }
                    }
                    .padding(.top, 10)
                    
                    // Trascrizione Box (Glassmorphism)
                    VStack(alignment: .leading) {
                        Text("Live Transcription")
                            .font(.caption)
                            .fontWeight(.bold)
                            .foregroundColor(.gray)
                            .textCase(.uppercase)
                        
                        ScrollView {
                            Text(speechRecognizer.transcribedText.isEmpty ? "Waiting for audio..." : speechRecognizer.transcribedText)
                                .font(.system(size: 16, weight: .regular, design: .default))
                                .foregroundColor(.white.opacity(0.9))
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.vertical, 8)
                        }
                    }
                    .padding()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Color.white.opacity(0.05))
                    .cornerRadius(24)
                    .overlay(RoundedRectangle(cornerRadius: 24).stroke(Color.white.opacity(0.1), lineWidth: 1))
                    .padding(.horizontal)
                    
                    // Riassunto Box (Glassmorphism)
                    if !llmService.summaryText.isEmpty {
                        VStack(alignment: .leading) {
                            HStack {
                                Text("AI Summary")
                                    .font(.caption)
                                    .fontWeight(.bold)
                                    .foregroundColor(brandAccent)
                                    .textCase(.uppercase)
                                Spacer()
                                Image(systemName: "sparkles")
                                    .foregroundColor(brandAccent)
                            }
                            
                            ScrollView {
                                Text(llmService.summaryText)
                                    .font(.system(size: 16, weight: .medium, design: .default))
                                    .foregroundColor(.white)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(.vertical, 8)
                            }
                        }
                        .padding()
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Color.white.opacity(0.08))
                        .cornerRadius(24)
                        .overlay(RoundedRectangle(cornerRadius: 24).stroke(brandAccent.opacity(0.3), lineWidth: 1))
                        .padding(.horizontal)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                    }
                    
                    Spacer()
                    
                    // Controlli
                    HStack(spacing: 30) {
                        Button(action: toggleRecording) {
                            ZStack {
                                Circle()
                                    .fill(speechRecognizer.isRecording ? Color.red.opacity(0.2) : brandSecondary.opacity(0.2))
                                    .frame(width: 80, height: 80)
                                    .scaleEffect(speechRecognizer.isRecording ? 1.2 : 1.0)
                                    .animation(speechRecognizer.isRecording ? Animation.easeInOut(duration: 1).repeatForever() : .default, value: speechRecognizer.isRecording)
                                
                                Circle()
                                    .fill(speechRecognizer.isRecording ? Color.red : brandSecondary)
                                    .frame(width: 60, height: 60)
                                    .shadow(color: speechRecognizer.isRecording ? .red : brandSecondary, radius: 10, x: 0, y: 0)
                                
                                Image(systemName: speechRecognizer.isRecording ? "stop.fill" : "mic.fill")
                                    .font(.system(size: 24, weight: .bold))
                                    .foregroundColor(.white)
                            }
                        }
                        
                        if !speechRecognizer.isRecording && !speechRecognizer.transcribedText.isEmpty {
                            VStack(spacing: 8) {
                                if !llmService.loadingProgress.isEmpty {
                                    Text(llmService.loadingProgress)
                                        .font(.caption2)
                                        .foregroundColor(brandAccent)
                                        .animation(.default, value: llmService.loadingProgress)
                                }
                                
                                Button(action: generateSummary) {
                                    HStack {
                                        if llmService.isProcessing {
                                            ProgressView()
                                                .progressViewStyle(CircularProgressViewStyle(tint: .white))
                                                .padding(.trailing, 4)
                                        } else {
                                            Image(systemName: "wand.and.stars")
                                        }
                                        Text("Summarize")
                                            .fontWeight(.bold)
                                    }
                                    .foregroundColor(.white)
                                    .padding(.horizontal, 24)
                                    .padding(.vertical, 16)
                                    .background(
                                        LinearGradient(gradient: Gradient(colors: [brandAccent, brandSecondary]), startPoint: .leading, endPoint: .trailing)
                                    )
                                    .cornerRadius(30)
                                    .shadow(color: brandAccent.opacity(0.5), radius: 10, x: 0, y: 5)
                                }
                                .disabled(llmService.isProcessing)
                                .transition(.scale)
                            }
                        }
                    }
                    .padding(.bottom, 40)
                }
            }
            .navigationBarHidden(true)
        }
        .preferredColorScheme(.dark)
        .onAppear {
            speechRecognizer.requestAuthorization()
        }
    }
    
    private func toggleRecording() {
        withAnimation(.spring()) {
            if !speechRecognizer.isRecording {
                llmService.summaryText = ""
            }
            speechRecognizer.toggleRecording()
        }
    }
    
    private func generateSummary() {
        withAnimation(.spring()) {
            Task {
                await llmService.generateSummary(text: speechRecognizer.transcribedText, context: selectedContext)
            }
        }
    }
}
