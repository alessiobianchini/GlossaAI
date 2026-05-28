import Foundation

enum MeetingLanguage: String, CaseIterable, Identifiable {
    case italian = "Italiano"
    case english = "English"
    case spanish = "Español"
    case french = "Français"
    case german = "Deutsch"
    
    var id: String { self.rawValue }
    
    var localeIdentifier: String {
        switch self {
        case .italian: return "it-IT"
        case .english: return "en-US"
        case .spanish: return "es-ES"
        case .french: return "fr-FR"
        case .german: return "de-DE"
        }
    }
}
