import Foundation

enum MeetingContext: String, CaseIterable, Identifiable {
    case general = "General"
    case developer = "Developer"
    case medical = "Medical"
    case friends = "Friends"
    case business = "Business Analysis"
    
    var id: String { self.rawValue }
}
