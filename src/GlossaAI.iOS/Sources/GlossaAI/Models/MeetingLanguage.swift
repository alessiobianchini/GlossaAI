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
    
    static func defaultForSystem() -> MeetingLanguage {
        let langCode = Locale.current.language.languageCode?.identifier ?? "en"
        switch langCode {
        case "it": return .italian
        case "es": return .spanish
        case "fr": return .french
        case "de": return .german
        default: return .english
        }
    }
}
