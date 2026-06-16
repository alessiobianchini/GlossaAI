import Foundation
import AVFoundation
import Speech

@MainActor
class SpeechRecognizer: ObservableObject {
    @Published var transcribedText = ""
    @Published var isRecording = false
    @Published var isTranscribingFile = false
    
    private var speechRecognizer = SFSpeechRecognizer(locale: Locale(identifier: "it-IT"))
    private var recognitionRequest: SFSpeechAudioBufferRecognitionRequest?
    private var recognitionTask: SFSpeechRecognitionTask?
    private let audioEngine = AVAudioEngine()
    
    func setLocale(identifier: String) {
        speechRecognizer = SFSpeechRecognizer(locale: Locale(identifier: identifier))
    }
    
    func requestAuthorization() {
        SFSpeechRecognizer.requestAuthorization { authStatus in
            DispatchQueue.main.async {
                switch authStatus {
                case .authorized:
                    print("Speech recognition authorized")
                default:
                    print("Speech recognition not authorized")
                }
            }
        }
        
        AVAudioApplication.requestRecordPermission { allowed in
            DispatchQueue.main.async {
                print("Microphone access: \(allowed)")
            }
        }
    }
    
    func toggleRecording() {
        if isRecording {
            stopTranscribing()
        } else {
            do {
                try startTranscribing()
            } catch {
                print("Error starting transcription: \(error.localizedDescription)")
            }
        }
    }
    
    private func startTranscribing() throws {
        // Reset state
        recognitionTask?.cancel()
        self.recognitionTask = nil
        self.transcribedText = ""
        
        // Configure audio session
        let audioSession = AVAudioSession.sharedInstance()
        try audioSession.setCategory(.record, mode: .measurement, options: .duckOthers)
        try audioSession.setActive(true, options: .notifyOthersOnDeactivation)
        
        recognitionRequest = SFSpeechAudioBufferRecognitionRequest()
        guard let recognitionRequest = recognitionRequest else {
            fatalError("Unable to create a SFSpeechAudioBufferRecognitionRequest object")
        }
        
        recognitionRequest.shouldReportPartialResults = true
        
        let inputNode = audioEngine.inputNode
        
        recognitionTask = speechRecognizer?.recognitionTask(with: recognitionRequest) { result, error in
            var isFinal = false
            
            if let result = result {
                DispatchQueue.main.async {
                    self.transcribedText = result.bestTranscription.formattedString
                }
                isFinal = result.isFinal
            }
            
            if error != nil || isFinal {
                self.audioEngine.stop()
                inputNode.removeTap(onBus: 0)
                
                self.recognitionRequest = nil
                self.recognitionTask = nil
                
                DispatchQueue.main.async {
                    self.isRecording = false
                }
            }
        }
        
        let recordingFormat = inputNode.outputFormat(forBus: 0)
        inputNode.installTap(onBus: 0, bufferSize: 1024, format: recordingFormat) { (buffer: AVAudioPCMBuffer, when: AVAudioTime) in
            self.recognitionRequest?.append(buffer)
        }
        
        audioEngine.prepare()
        try audioEngine.start()
        
        DispatchQueue.main.async {
            self.isRecording = true
        }
    }
    
    func stopTranscribing() {
        audioEngine.stop()
        audioEngine.inputNode.removeTap(onBus: 0)
        recognitionRequest?.endAudio()
        
        DispatchQueue.main.async {
            self.isRecording = false
        }
    }
    
    // File transcription
    func transcribeFile(url: URL) async {
        DispatchQueue.main.async {
            self.isTranscribingFile = true
            self.transcribedText = ""
        }
        
        let request = SFSpeechURLRecognitionRequest(url: url)
        request.shouldReportPartialResults = true
        
        // Since we await the completion block natively using continuations is possible, 
        // but SFSpeechRecognizer uses a closure-based callback that returns multiple times.
        // We'll wrap it and wait for the final result.
        
        await withCheckedContinuation { continuation in
            recognitionTask = speechRecognizer?.recognitionTask(with: request) { result, error in
                var isFinal = false
                if let result = result {
                    DispatchQueue.main.async {
                        self.transcribedText = result.bestTranscription.formattedString
                    }
                    isFinal = result.isFinal
                }
                
                if error != nil || isFinal {
                    self.recognitionTask = nil
                    DispatchQueue.main.async {
                        self.isTranscribingFile = false
                        if let error = error {
                            let formatString = String(localized: "Error transcribing file: %@")
                            self.transcribedText = String(format: formatString, error.localizedDescription)
                        }
                    }
                    continuation.resume()
                }
            }
            
            // If creation failed immediately
            if recognitionTask == nil {
                DispatchQueue.main.async {
                    self.isTranscribingFile = false
                    self.transcribedText = String(localized: "Unable to start recognition.")
                }
                continuation.resume()
            }
        }
    }
}
