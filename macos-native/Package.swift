// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "DailyLedgerNative",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "DailyLedgerNative", targets: ["DailyLedgerNative"])],
    targets: [
        .target(name: "LedgerCore"),
        .executableTarget(name: "DailyLedgerNative", dependencies: ["LedgerCore"]),
        .testTarget(name: "LedgerCoreTests", dependencies: ["LedgerCore"])
    ]
)
