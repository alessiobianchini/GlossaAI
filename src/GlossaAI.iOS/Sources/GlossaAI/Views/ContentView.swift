import SwiftUI
import UniformTypeIdentifiers

struct ContentView: View {
    @StateObject private var speechRecognizer = SpeechRecognizer()
    @StateObject private var llmService = LLMService()
    
    @State private var selectedContext: MeetingContext = .general
    @State private var selectedLanguage: MeetingLanguage = MeetingLanguage.defaultForSystem()
    @State private var showFilePicker = false
    
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
                
                VStack(spacing: 20) {
                    // Selezione Context e Language (Orizzontale)
                    VStack(spacing: 16) {
                        // Context
                        VStack(alignment: .leading, spacing: 8) {
                            Text("Meeting Context")
                                .font(.caption)
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
                                                .font(.system(size: 13, weight: .medium, design: .rounded))
                                                .padding(.horizontal, 16)
                                                .padding(.vertical, 8)
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
                        
                        // Language
                        VStack(alignment: .leading, spacing: 8) {
                            Text("Language")
                                .font(.caption)
                                .fontWeight(.semibold)
                                .foregroundColor(.gray)
                                .padding(.horizontal)
                            
                            ScrollView(.horizontal, showsIndicators: false) {
                                HStack(spacing: 12) {
                                    ForEach(MeetingLanguage.allCases) { lang in
                                        Button(action: {
                                            withAnimation(.spring()) {
                                                selectedLanguage = lang
                                                speechRecognizer.setLocale(identifier: lang.localeIdentifier)
                                            }
                                        }) {
                                            Text(lang.rawValue)
                                                .font(.system(size: 13, weight: .medium, design: .rounded))
                                                .padding(.horizontal, 16)
                                                .padding(.vertical, 8)
                                                .background(
                                                    selectedLanguage == lang ?
                                                    LinearGradient(gradient: Gradient(colors: [brandAccent, brandSecondary]), startPoint: .leading, endPoint: .trailing) :
                                                        LinearGradient(gradient: Gradient(colors: [Color.white.opacity(0.1), Color.white.opacity(0.05)]), startPoint: .leading, endPoint: .trailing)
                                                )
                                                .foregroundColor(selectedLanguage == lang ? .white : .gray)
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
                    }
                    .padding(.top, 10)
                    
                    // Trascrizione Box
                    VStack(alignment: .leading) {
                        HStack {
                            Text("Live Transcription")
                                .font(.caption)
                                .fontWeight(.bold)
                                .foregroundColor(.gray)
                                .textCase(.uppercase)
                            
                            Spacer()
                            
                            if !speechRecognizer.transcribedText.isEmpty {
                                ShareLink(item: speechRecognizer.transcribedText) {
                                    Image(systemName: "square.and.arrow.up")
                                        .foregroundColor(brandSecondary)
                                }
                            }
                        }
                        
                        ScrollView {
                            if speechRecognizer.isTranscribingFile {
                                HStack {
                                    ProgressView()
                                        .progressViewStyle(CircularProgressViewStyle(tint: brandSecondary))
                                    Text("Processing file...")
                                        .foregroundColor(.gray)
                                        .padding(.leading, 8)
                                }
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.vertical, 8)
                            } else {
                                Text(speechRecognizer.transcribedText.isEmpty ? "Waiting for audio..." : speechRecognizer.transcribedText)
                                    .font(.system(size: 15, weight: .regular, design: .default))
                                    .foregroundColor(.white.opacity(0.9))
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(.vertical, 8)
                            }
                        }
                    }
                    .padding()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Color.white.opacity(0.05))
                    .cornerRadius(20)
                    .overlay(RoundedRectangle(cornerRadius: 20).stroke(Color.white.opacity(0.1), lineWidth: 1))
                    .padding(.horizontal)
                    
                    // Riassunto Box
                    if !llmService.summaryText.isEmpty {
                        VStack(alignment: .leading) {
                            HStack {
                                Text("AI Summary")
                                    .font(.caption)
                                    .fontWeight(.bold)
                                    .foregroundColor(brandAccent)
                                    .textCase(.uppercase)
                                
                                Spacer()
                                
                                ShareLink(item: llmService.summaryText) {
                                    Image(systemName: "square.and.arrow.up")
                                        .foregroundColor(brandAccent)
                                }
                            }
                            
                            ScrollView {
                                Text(llmService.summaryText)
                                    .font(.system(size: 15, weight: .medium, design: .default))
                                    .foregroundColor(.white)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(.vertical, 8)
                            }
                        }
                        .padding()
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(Color.white.opacity(0.08))
                        .cornerRadius(20)
                        .overlay(RoundedRectangle(cornerRadius: 20).stroke(brandAccent.opacity(0.3), lineWidth: 1))
                        .padding(.horizontal)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                    }
                    
                    Spacer()
                    
                    // Controlli
                    HStack(spacing: 20) {
                        // Mic Button
                        Button(action: toggleRecording) {
                            ZStack {
                                Circle()
                                    .fill(speechRecognizer.isRecording ? Color.red.opacity(0.2) : brandSecondary.opacity(0.2))
                                    .frame(width: 70, height: 70)
                                    .scaleEffect(speechRecognizer.isRecording ? 1.2 : 1.0)
                                    .animation(speechRecognizer.isRecording ? Animation.easeInOut(duration: 1).repeatForever() : .default, value: speechRecognizer.isRecording)
                                
                                Circle()
                                    .fill(speechRecognizer.isRecording ? Color.red : brandSecondary)
                                    .frame(width: 50, height: 50)
                                    .shadow(color: speechRecognizer.isRecording ? .red : brandSecondary, radius: 10, x: 0, y: 0)
                                
                                Image(systemName: speechRecognizer.isRecording ? "stop.fill" : "mic.fill")
                                    .font(.system(size: 20, weight: .bold))
                                    .foregroundColor(.white)
                            }
                        }
                        .disabled(speechRecognizer.isTranscribingFile)
                        
                        // Import File Button
                        if !speechRecognizer.isRecording && speechRecognizer.transcribedText.isEmpty {
                            Button(action: {
                                showFilePicker = true
                            }) {
                                HStack {
                                    Image(systemName: "folder.fill")
                                    Text("Import")
                                        .fontWeight(.bold)
                                }
                                .font(.system(size: 14))
                                .foregroundColor(.white)
                                .padding(.horizontal, 20)
                                .padding(.vertical, 14)
                                .background(Color.white.opacity(0.1))
                                .cornerRadius(25)
                                .overlay(RoundedRectangle(cornerRadius: 25).stroke(Color.white.opacity(0.2), lineWidth: 1))
                            }
                            .disabled(speechRecognizer.isTranscribingFile)
                        }
                        
                        // Summarize Button
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
                                    .font(.system(size: 14))
                                    .foregroundColor(.white)
                                    .padding(.horizontal, 20)
                                    .padding(.vertical, 14)
                                    .background(
                                        LinearGradient(gradient: Gradient(colors: [brandAccent, brandSecondary]), startPoint: .leading, endPoint: .trailing)
                                    )
                                    .cornerRadius(25)
                                    .shadow(color: brandAccent.opacity(0.5), radius: 10, x: 0, y: 5)
                                }
                                .disabled(llmService.isProcessing)
                            }
                            .transition(.scale)
                        }
                    }
                    .padding(.bottom, 30)
                }
            }
            .navigationBarHidden(true)
            .fileImporter(
                isPresented: $showFilePicker,
                allowedContentTypes: [.audio, .movie],
                allowsMultipleSelection: false
            ) { result in
                switch result {
                case .success(let urls):
                    guard let url = urls.first else { return }
                    // Richiede l'accesso al file di sicurezza (document picker)
                    if url.startAccessingSecurityScopedResource() {
                        Task {
                            await speechRecognizer.transcribeFile(url: url)
                            url.stopAccessingSecurityScopedResource()
                        }
                    } else {
                        print("Impossibile accedere al file.")
                    }
                case .failure(let error):
                    print("Errore selezione file: \(error.localizedDescription)")
                }
            }
        }
        .preferredColorScheme(.dark)
        .onAppear {
            speechRecognizer.requestAuthorization()
            speechRecognizer.setLocale(identifier: selectedLanguage.localeIdentifier)
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
