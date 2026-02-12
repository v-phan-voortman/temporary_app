# Building a Sustainable Capacitor FCM Notification Plugin: Architecture and Maintenance Strategy

**No existing Capacitor plugin supports the full notification feature set you need — expandable styles, progress bars, call notifications, Live Activities, and communication notifications are all absent from every published plugin.** Building a custom plugin is unavoidable, but the architecture choices you make now will determine whether maintenance consumes hours or weeks per OS release cycle. The key insight: split the plugin into a thin, stable FCM messaging layer and a separate, modular notification rendering layer, because FCM token management changes rarely while notification presentation APIs change annually on both platforms [(Ionic Team, 2024a)](#ionic-2024a). This two-plugin architecture, combined with Capacitor's bridge-implementation separation pattern [(Capacitor, 2024a)](#capacitor-2024a), capability-based feature detection, and platform-specific isolation modules, can reduce your maintenance burden from "rewrite on every OS update" to "add a new module when a new feature ships."

## The ecosystem gap that forces a custom build

Every existing Capacitor push notification plugin — the official `@capacitor/push-notifications`, capawesome's `@capacitor-firebase/messaging`, and the community `@capacitor-community/fcm` — stops at basic functionality: token registration, permission handling, foreground/background message delivery, and simple notification display [(Ionic Team, 2024b)](#ionic-2024b); [(Capawesome Team, 2024)](#capawesome-2024); [(Capacitor Community, 2024)](#capacitor-community-2024). **None supports expandable notifications, progress bars, call-style notifications, Live Activities, or communication notifications.** Issue #361 requesting progress bar support on the official plugin has been open since April 2021 with no implementation planned [(Ionic Team, 2021)](#ionic-2021).

The official plugin covers FCM token lifecycle, notification channels on Android, and basic presentation options on iOS [(Ionic Team, 2024b)](#ionic-2024b). Capawesome adds topic subscription, web support, and FCM auto-init control [(Capawesome Team, 2024)](#capawesome-2024). But both treat the notification itself as opaque — they pass payloads to the OS default display mechanism and stop there. For custom notification rendering, you must write native code, and no existing plugin provides the bridge.

This means extending an existing plugin is architecturally wrong for your use case. These plugins were designed as thin message-passing bridges, not notification rendering engines. Grafting a full notification rendering system onto them would fight their design rather than leverage it. Instead, **use an existing plugin for FCM messaging infrastructure and build a companion plugin exclusively for notification rendering**. This isolates the fast-changing notification presentation code from the stable messaging plumbing.

## A two-plugin architecture that isolates change

The highest-leverage architectural decision is splitting responsibilities across two distinct Capacitor plugins:

**Plugin 1: FCM Messaging (stable layer).** Handles token management, message reception, topic subscription, and permission requests. You can use `@capacitor-firebase/messaging` directly or fork a minimal version [(Capawesome Team, 2024)](#capawesome-2024). This layer changes only when Firebase SDK or Capacitor itself ships breaking changes — roughly once per year, with migration tooling provided [(Capacitor, 2024b)](#capacitor-2024b).

**Plugin 2: Notification Renderer (active development layer).** Handles all notification display, styling, and interaction — expandable styles, progress bars, call notifications, Live Activities, custom layouts, and actionable notifications. This is where OS-level API churn concentrates and where your architecture must absorb change gracefully.

The Notification Renderer plugin should follow Capacitor's recommended two-class pattern on each platform: a thin bridge class (`NotificationRendererPlugin.java` / `NotificationRendererPlugin.swift`) that handles Capacitor communication, and thick implementation classes that contain all notification logic [(Capacitor, 2024a)](#capacitor-2024a); [(Ionic, 2024)](#ionic-2024). Critically, the implementation classes should **never import Capacitor** — they should be pure native code, testable independently, and reusable outside the plugin context.

```
notification-renderer-plugin/
├── src/
│   ├── definitions.ts           # Stable JS API contract
│   ├── index.ts                 # registerPlugin + exports
│   └── web.ts                   # Web fallback (limited)
├── android/src/main/java/.../
│   ├── NotificationRendererPlugin.kt   # Bridge (thin)
│   ├── NotificationFactory.kt          # Builds notifications by type
│   ├── styles/
│   │   ├── BasicNotificationBuilder.kt
│   │   ├── ExpandableNotificationBuilder.kt
│   │   ├── ProgressNotificationBuilder.kt
│   │   ├── CallNotificationBuilder.kt
│   │   └── LiveUpdateNotificationBuilder.kt
│   ├── ChannelManager.kt
│   └── PermissionHelper.kt
├── ios/Sources/NotificationRenderer/
│   ├── NotificationRendererPlugin.swift  # Bridge (thin)
│   ├── NotificationFactory.swift
│   ├── handlers/
│   │   ├── BasicNotificationHandler.swift
│   │   ├── ActionableNotificationHandler.swift
│   │   ├── LiveActivityManager.swift        # @available(iOS 16.1, *)
│   │   ├── CommunicationHandler.swift       # @available(iOS 15, *)
│   │   └── TimeSensitiveHandler.swift       # @available(iOS 15, *)
│   └── extensions/
│       └── (Notification Service Extension code)
├── Package.swift
└── package.json
```

The **NotificationFactory** on each platform acts as a facade, routing rendering requests to the appropriate builder/handler. New notification types become new files — you never modify existing builders to add new ones, following the Open/Closed principle.

## Designing the TypeScript API for platform asymmetry

The JavaScript API must accommodate fundamentally different capabilities across platforms without leaking platform internals. The most effective pattern combines a **unified base interface** with **platform-specific option namespaces** and a **runtime capability query**.

```typescript
export interface NotificationRendererPlugin {
  // Capability detection — call first, branch on results
  getCapabilities(): Promise<NotificationCapabilities>;
  
  // Unified notification display
  show(options: NotificationOptions): Promise<{ id: string }>;
  update(options: UpdateNotificationOptions): Promise<void>;
  cancel(options: { id: string }): Promise<void>;
  
  // Platform-specific features exposed through dedicated methods
  startLiveActivity?(options: LiveActivityOptions): Promise<{ activityId: string }>;
  updateLiveActivity?(options: UpdateLiveActivityOptions): Promise<void>;
  endLiveActivity?(options: { activityId: string }): Promise<void>;
  
  // Events
  addListener(event: 'notificationAction', fn: (data: ActionEvent) => void): Promise<PluginListenerHandle>;
}

export interface NotificationCapabilities {
  expandableNotifications: boolean;
  progressNotifications: boolean;
  callStyleNotifications: boolean;
  liveActivities: boolean;          // iOS 16.1+ only
  communicationNotifications: boolean; // iOS 15+ only
  timeSensitiveNotifications: boolean; // iOS 15+ only
  notificationChannels: boolean;     // Android 8+ only
  customLayouts: boolean;            // Android only
}

export interface NotificationOptions {
  id: string;
  title: string;
  body: string;
  style?: 'basic' | 'bigText' | 'bigPicture' | 'inbox' | 'messaging' | 'progress' | 'call';
  // Platform-specific options namespaced to prevent collision
  android?: AndroidNotificationOptions;
  ios?: IOSNotificationOptions;
}
```

The `getCapabilities()` method is critical. It lets consuming code branch on actual device support rather than hardcoded platform checks. On Android, the implementation checks `Build.VERSION.SDK_INT` for features like CallStyle (API 31+) and ProgressStyle (API 36+). On iOS, it checks `#available` for Live Activities (iOS 16.1+), communication notifications (iOS 15+), and time-sensitive notifications (iOS 15+). **This makes the JS layer immune to OS version changes** — when Android 17 adds a new notification style, you add a new capability flag and a new builder class without touching existing code.

Mark platform-exclusive features as optional methods (the trailing `?` on `startLiveActivity`). This makes TypeScript enforce that consumers check availability before calling. Use `undefined` returns rather than `null` for unsupported features, per Capacitor conventions.

## Deep dive: Why these design patterns minimize maintenance

The architectural choices above aren't arbitrary — each pattern directly addresses a specific category of maintenance pain that emerges when OS vendors ship breaking changes. Understanding *why* these patterns work helps you apply them correctly and extend them as your needs evolve.

### Pattern 1: Plugin separation via responsibility boundaries

**The pattern:** Split FCM messaging (token lifecycle, message delivery) from notification rendering (display, styling, interaction) into two separate Capacitor plugins that communicate through data payloads.

**Why it works:**

The core insight is that **change velocity differs by an order of magnitude** across these concerns. Firebase SDK updates happen roughly annually and are well-documented with migration guides. FCM token registration hasn't fundamentally changed since FCM v1 launched in 2016 — you still request permission, get a token, send it to your server, and receive messages via a service. This is infrastructure code with high stability.

Notification presentation APIs change constantly. Android adds a new NotificationStyle every 1-2 OS versions. iOS introduced Live Activities (iOS 16.1), push-to-start for Live Activities (iOS 17.2), and broadcast push notifications (iOS 18) in rapid succession. Each requires new native APIs, new permission handling, and new lifecycle management. This is feature code with high volatility.

**The alternative — single plugin — creates coupling that amplifies maintenance burden.** Consider what happens when iOS 19 changes ActivityKit's token refresh behavior:

- **With separation:** You update `LiveActivityManager.swift` in the renderer plugin. FCM messaging is untouched. Your app code that uses basic notifications sees zero impact.
- **Without separation:** The ActivityKit change forces you to test the entire plugin. Because the FCM and notification code share state (device tokens, permission states), you risk introducing bugs in FCM message delivery while fixing ActivityKit. Users who don't use Live Activities still need to update the plugin to get FCM bug fixes.

The separation also enables **independent release cycles**. You can ship a new notification style without waiting for FCM SDK updates, and vice versa.

### Pattern 2: Factory pattern with isolated builders

**The pattern:** Each notification style (basic, expandable, progress, call) has its own builder class. A factory inspects incoming payloads and delegates to the appropriate builder.

**Why it works:**

This pattern applies the **Open/Closed Principle** — open for extension, closed for modification. When Android 17 ships a new notification style, you:

1. Add a new builder class (e.g., `NewStyleNotificationBuilder.kt`)
2. Add a case to the factory's switch statement
3. Add a capability flag to `getCapabilities()`

Crucially, you **never modify existing builder classes**. `BasicNotificationBuilder.kt` from day one never changes, which means:

- **Zero regression risk for existing notification types** — if your basic notifications worked on Android 12, they still work after you add Android 17 support
- **Incremental testing scope** — you only test the new builder, not the entire plugin
- **Contributor-friendly architecture** — a developer can add progress notification support without understanding call-style notifications

**The alternative — conditional logic in a monolithic builder — creates combinatorial complexity:**

```kotlin
// Anti-pattern: monolithic builder with conditionals
fun buildNotification(data: Map<String, String>): Notification {
    val builder = NotificationCompat.Builder(context, channelId)
    
    // Basic properties
    builder.setContentTitle(data["title"])
    builder.setContentText(data["body"])
    
    // Style-specific logic branches
    if (data["style"] == "bigText") {
        builder.setStyle(NotificationCompat.BigTextStyle()
            .bigText(data["bigText"]))
    } else if (data["style"] == "progress") {
        if (Build.VERSION.SDK_INT >= 36) {
            builder.setStyle(NotificationCompat.ProgressStyle()
                .setProgress(data["progress"]?.toInt() ?: 0))
        } else {
            builder.setProgress(100, data["progress"]?.toInt() ?: 0, false)
        }
    } else if (data["style"] == "call") {
        if (Build.VERSION.SDK_INT >= 31) {
            // CallStyle setup
        } else {
            // Fallback to high-priority with actions
        }
    }
    // More branches for inbox, messaging, custom layouts...
    
    return builder.build()
}
```

This code is **unmaintainable** because:
- Each new style adds N new lines of conditional logic
- Version checks are scattered throughout, making it unclear which code runs on which OS
- Testing requires exercising all combinations of (style × OS version × data variations)
- Bug fixes in one style risk breaking others through shared state

The factory pattern eliminates this by making each builder **independently correct** — `CallNotificationBuilder` contains exactly the code needed for call-style notifications and nothing else.

### Pattern 3: Capability detection over platform checks

**The pattern:** The JavaScript API queries native capabilities via `getCapabilities()` at runtime, then branches on actual device support rather than hardcoded `if (platform === 'ios')` checks.

**Why it works:**

Platform checks are **proxy variables** — you're checking the platform when you really care about feature availability. This creates three failure modes:

1. **False positives:** iOS 15 devices don't support Live Activities (iOS 16.1+ only), but your code treats all iOS as equivalent
2. **False negatives:** Android forks (Samsung OneUI, MIUI) sometimes backport features to older API levels
3. **Maintenance drift:** When iOS 19 adds a new feature, you must update every `if (platform === 'ios')` check to `if (platform === 'ios' && version >= 19)`

Capability detection queries the actual runtime environment:

```typescript
// Capability-based branching
const caps = await NotificationRenderer.getCapabilities();

if (caps.liveActivities) {
  await NotificationRenderer.startLiveActivity({ ... });
} else if (caps.progressNotifications) {
  await NotificationRenderer.show({ style: 'progress', ... });
} else {
  await NotificationRenderer.show({ style: 'basic', ... });
}
```

This code is **future-proof** because:
- When iOS 19 adds a feature, you add a new capability flag. Existing `caps.liveActivities` checks are unchanged.
- When Android backports a feature, users get it automatically without app updates (the capability flag reflects the actual device support).
- The same code works across OS versions — a 2024 device and a 2028 device both report accurate capabilities.

The native implementation on Android:

```kotlin
override fun getCapabilities(call: PluginCall) {
    val caps = JSObject()
    caps.put("expandableNotifications", true) // All modern Android
    caps.put("progressNotifications", Build.VERSION.SDK_INT >= 36)
    caps.put("callStyleNotifications", Build.VERSION.SDK_INT >= 31)
    caps.put("liveActivities", false) // iOS-only
    caps.put("notificationChannels", Build.VERSION.SDK_INT >= 26)
    call.resolve(caps)
}
```

And on iOS:

```swift
@objc func getCapabilities(_ call: CAPPluginCall) {
    var caps: [String: Any] = [:]
    caps["expandableNotifications"] = true
    caps["progressNotifications"] = false // Android-only
    if #available(iOS 16.1, *) {
        caps["liveActivities"] = true
    } else {
        caps["liveActivities"] = false
    }
    if #available(iOS 15.0, *) {
        caps["communicationNotifications"] = true
        caps["timeSensitiveNotifications"] = true
    } else {
        caps["communicationNotifications"] = false
        caps["timeSensitiveNotifications"] = false
    }
    call.resolve(caps)
}
```

The version checks are **centralized** in one method. When iOS 19 adds a feature, you update this method and add a new capability flag. All consuming code automatically adapts.

### Pattern 4: Bridge-implementation separation

**The pattern:** The Capacitor bridge class (`NotificationRendererPlugin.kt`) handles only plugin communication — argument parsing, return value serialization, error handling. All business logic lives in pure native classes (`NotificationFactory`, `BasicNotificationBuilder`) that never import Capacitor.

**Why it works:**

This pattern makes your code **testable without Capacitor infrastructure**. Testing the bridge class requires running in a Capacitor environment (emulator or device). Testing pure native classes requires only JUnit + Mockito on Android or XCTest on iOS, which run in milliseconds.

Example structure:

```kotlin
// Bridge class - thin wrapper
@NativePlugin
class NotificationRendererPlugin : Plugin() {
    private val factory = NotificationFactory()
    
    @PluginMethod
    fun show(call: PluginCall) {
        val data = call.data.toMap()
        try {
            val id = factory.buildAndShow(context, data)
            call.resolve(JSObject().put("id", id))
        } catch (e: Exception) {
            call.reject("Failed to show notification", e)
        }
    }
}

// Implementation class - no Capacitor imports
class NotificationFactory {
    fun buildAndShow(context: Context, data: Map<String, Any>): String {
        val style = data["style"] as? String ?: "basic"
        val builder = when (style) {
            "basic" -> BasicNotificationBuilder()
            "progress" -> ProgressNotificationBuilder()
            "call" -> CallNotificationBuilder()
            else -> BasicNotificationBuilder()
        }
        return builder.build(context, data).also { notification ->
            NotificationManagerCompat.from(context)
                .notify(data["id"] as String, notification)
        }
    }
}
```

The test suite never touches the bridge:

```kotlin
class NotificationFactoryTest {
    @Test
    fun `progress notification includes progress bar`() {
        val context = mock(Context::class.java)
        val data = mapOf(
            "id" to "test-123",
            "style" to "progress",
            "title" to "Uploading",
            "progress" to 45
        )
        
        val factory = NotificationFactory()
        val notification = factory.buildAndShow(context, data)
        
        // Assert notification has ProgressStyle
        // Assert progress value is 45
        // No Capacitor dependencies needed
    }
}
```

This separation also makes your code **reusable outside Capacitor**. If you later want to build a React Native plugin, you can copy the entire `NotificationFactory`, `BasicNotificationBuilder`, etc. and only rewrite the thin bridge layer. The 90% of the code that implements notification logic is platform-native and portable.

### Why this architecture minimizes maintenance

These patterns combine to create **localized change**:

- When Android 17 ships: add one builder class, one factory case, one capability flag
- When iOS updates ActivityKit: modify one file (`LiveActivityManager.swift`) guarded by availability checks
- When Capacitor 8 ships: update thin bridge classes, implementation classes are unchanged
- When you need a new feature: add a new builder, existing features untouched

The alternative — monolithic plugin with conditional logic — creates **cascading change**:

- Android 17: modify the monolithic builder, risking regressions in all styles
- iOS ActivityKit update: change flows through shared state, risking FCM token handling
- Capacitor 8: bridge and implementation are entangled, requiring full retest
- New feature: add branches to existing functions, increasing cyclomatic complexity

The key metric is **blast radius** — how many lines of code must change when the OS changes. This architecture minimizes it by making each concern independently correct and independently changeable.

## Android: NotificationCompat absorbs most version churn

Android's notification API has undergone significant breaking changes roughly every two years — **channels in Android 8, custom decoration enforcement in Android 12, POST_NOTIFICATIONS permission in Android 13, foreground service types in Android 14, and Live Updates (ProgressStyle) in Android 16** [(Android Developers, 2024a,](#android-2024a) [2024b;](#android-2024b) [Expert App Devs, 2024)](#expertappdevs-2024). However, AndroidX's `NotificationCompat` abstracts most of this.

`NotificationCompat.Builder` handles backward-compatible construction of all standard styles: `BigTextStyle`, `BigPictureStyle`, `InboxStyle`, `MessagingStyle`, `CallStyle`, and now `ProgressStyle` (Android Developers, 2024c, 2024d). These degrade gracefully on older devices — a `CallStyle` notification on a pre-Android 12 device renders as a high-priority notification with action buttons. **Always use `NotificationCompat`, never `Notification.Builder` directly**, unless you need an API exclusively available on the platform class [(Wix, 2019)](#wix-2019).

What NotificationCompat does **not** cover, requiring manual version checks:

- **NotificationChannel creation** (API 26+) — `NotificationChannel` has no compat equivalent [(Android Developers, 2024c)](#android-2024c)
- **POST_NOTIFICATIONS runtime permission** (API 33+) — must request explicitly [(Android Developers, 2024b)](#android-2024b)
- **PendingIntent mutability flags** (API 31+) — must specify `FLAG_IMMUTABLE` or `FLAG_MUTABLE` [(Android Developers, 2024a)](#android-2024a)
- **Foreground service types** (API 34+) — manifest and runtime declarations required ([(Android Developers, 2024e;](#android-2024e) [(Google, 2024)](#google-2024))
- **Full-screen intent restrictions** (API 34+) — limited to calling/alarm apps [(Google, 2024)](#google-2024)

For FCM message handling, **use data-only messages exclusively**. When FCM delivers a combined notification+data message to a backgrounded app, the system auto-displays the notification portion and the data payload is only accessible if the user taps it. Data-only messages always route through `onMessageReceived()`, giving you full control over notification construction regardless of app state ([(Firebase, 2024a;](#firebase-2024a) [(Sqlpey, 2024)](#sqlpey-2024)). This architectural choice eliminates an entire class of "notification not showing correctly" bugs.

Structure your Android notification code with a factory pattern where each notification style has its own builder class. The factory inspects the incoming data, checks device capabilities via `Build.VERSION.SDK_INT`, and delegates to the appropriate builder. New Android versions mean new builder classes, not modifications to existing ones.

## iOS: stable core with a volatile Live Activities frontier

iOS notification architecture has a fundamentally different maintenance profile than Android. The **core `UserNotifications` framework has been stable since iOS 10** — `UNUserNotificationCenter`, `UNNotificationContent`, categories, and actions have seen only additive changes over nine years [(Pushwoosh, 2024)](#pushwoosh-2024). Apple's approach of long deprecation cycles (3-5+ years) and additive APIs means your basic push notification, actionable notification, and local notification code will rarely need changes.

The maintenance risk concentrates in three areas, ranked by volatility:

**ActivityKit (Live Activities) is by far the highest-risk component.** It has seen major API changes in every release since introduction: iOS 16.1 launched it, iOS 16.2 refined the API, iOS 17 added interactive elements and StandBy support, **iOS 17.2 introduced push-to-start** (a major new capability), and iOS 18 added broadcast push notifications while throttling update frequency from every second to 5-15 seconds ([(Apple Developer Forums, 2024;](#apple-forums-2024) [(Batch, 2022;](#batch-2022) [(Harkhani, 2024)](#harkhani-2024)). Push-to-start token behavior has known inconsistencies across OS versions. Expect annual breaking changes here for at least two more years.

**Communication notifications** (iOS 15+) use `INSendMessageIntent` from SiriKit, but Apple is gradually migrating from SiriKit Intents to the App Intents framework (introduced iOS 16). This transition may eventually require migrating communication notification code [(Apple, 2021)](#apple-2021).

**Time-sensitive notifications** (iOS 15+) require a specific entitlement (`com.apple.developer.usernotifications.time-sensitive`) and use the `UNNotificationInterruptionLevel` enum ([(Apple, 2021;](#apple-2021) [(Home Assistant, 2021)](#homeassistant-2021)). This API has been stable since introduction and is low-maintenance.

Architecturally, **isolate ActivityKit code completely** — it should live in its own file guarded by `@available(iOS 16.1, *)`, with its own manager class. Live Activities also require a WidgetKit extension target, which means your plugin must guide users through adding a widget extension to their Xcode project. This is inherently platform-specific setup that can't be automated through the Capacitor plugin alone. Similarly, communication notifications require a Notification Service Extension target [(NotifyVisitors, 2024)](#notifyvisitors-2024). Document these Xcode configuration steps meticulously — **the setup documentation is as important as the code for iOS maintenance**.

For FCM on iOS specifically, decide early whether to use method swizzling. If your Capacitor app uses SwiftUI or multiple push-related libraries, **disable swizzling** (`FirebaseAppDelegateProxyEnabled = NO` in Info.plist) and handle APNs token mapping manually ([(Firebase, 2024b;](#firebase-2024b) [(Xamarin, 2024)](#xamarin-2024)). This eliminates a category of mysterious token-related bugs and makes the data flow explicit.

## Remote notification management: updates and withdrawals across devices

A critical requirement you've identified is the ability to update or withdraw notifications remotely — either from a backend server or from actions taken on another device belonging to the same user. For example, when a user marks a notification as read on their phone, the notification should disappear on their tablet; when backend processing completes, a "processing" notification should update to show "complete." This requires careful architecture around notification state synchronization and message routing.

### The core challenge: notifications are local state

Notifications exist only on the device where they're displayed. When your backend sends an FCM message that creates a notification on Device A, **that notification object lives only on Device A**. The OS provides no built-in mechanism for one device to query or modify another device's notifications. Your architecture must build this synchronization layer.

The fundamental pattern: treat notifications as **stateful entities with lifecycle events** rather than fire-and-forget displays. Each notification has:

- A **globally unique ID** shared across all devices (e.g., `order-123-status`, `message-456-from-alice`)
- A **current state** (pending, updated, withdrawn, expired)
- A **content version** to handle out-of-order message delivery
- A **timestamp** for conflict resolution

### Architecture for remote notification updates

The most maintainable approach uses FCM data messages with special `action` payloads that instruct the device what to do with its local notification state:

```json
// Backend sends this to update a notification
{
  "data": {
    "action": "UPDATE_NOTIFICATION",
    "notificationId": "order-123-status",
    "version": "2",
    "title": "Order Complete",
    "body": "Your order has been delivered",
    "style": "basic",
    "timestamp": "2026-02-11T14:30:00Z"
  }
}

// Backend sends this to remove a notification
{
  "data": {
    "action": "CANCEL_NOTIFICATION",
    "notificationId": "order-123-status",
    "reason": "dismissed_on_other_device",
    "timestamp": "2026-02-11T14:35:00Z"
  }
}

// Backend sends this to show a new notification
{
  "data": {
    "action": "SHOW_NOTIFICATION",
    "notificationId": "order-123-status",
    "version": "1",
    "title": "Order Processing",
    "body": "We're preparing your order",
    "style": "progress",
    "progress": 45,
    "timestamp": "2026-02-11T14:00:00Z"
  }
}
```

Your FCM message handler inspects the `action` field and routes accordingly:

```kotlin
// Android FCM service
override fun onMessageReceived(message: RemoteMessage) {
    val data = message.data
    when (data["action"]) {
        "SHOW_NOTIFICATION" -> {
            val notification = factory.buildNotification(context, data)
            notificationManager.notify(data["notificationId"], 0, notification)
            stateManager.recordNotification(
                id = data["notificationId"]!!,
                version = data["version"]?.toInt() ?: 1,
                timestamp = parseTimestamp(data["timestamp"])
            )
        }
        
        "UPDATE_NOTIFICATION" -> {
            // Check version to avoid applying stale updates
            if (stateManager.shouldApplyUpdate(
                id = data["notificationId"]!!,
                version = data["version"]?.toInt() ?: 0
            )) {
                val notification = factory.buildNotification(context, data)
                notificationManager.notify(data["notificationId"], 0, notification)
                stateManager.updateNotification(
                    id = data["notificationId"]!!,
                    version = data["version"]?.toInt() ?: 0,
                    timestamp = parseTimestamp(data["timestamp"])
                )
            }
        }
        
        "CANCEL_NOTIFICATION" -> {
            notificationManager.cancel(data["notificationId"], 0)
            stateManager.removeNotification(data["notificationId"]!!)
        }
    }
}
```

### Notification state manager: handling concurrent updates

The `NotificationStateManager` is a new component in your plugin that tracks which notifications are currently displayed and their versions. This prevents race conditions when multiple update messages arrive out of order:

```kotlin
class NotificationStateManager(private val prefs: SharedPreferences) {
    private data class NotificationState(
        val id: String,
        val version: Int,
        val timestamp: Long
    )
    
    fun shouldApplyUpdate(id: String, version: Int): Boolean {
        val current = getState(id) ?: return true
        return version > current.version
    }
    
    fun recordNotification(id: String, version: Int, timestamp: Long) {
        val state = NotificationState(id, version, timestamp)
        // Persist to SharedPreferences or Room database
        saveState(id, state)
    }
    
    fun updateNotification(id: String, version: Int, timestamp: Long) {
        val current = getState(id) ?: return
        if (version > current.version) {
            recordNotification(id, version, timestamp)
        }
    }
    
    fun removeNotification(id: String) {
        prefs.edit().remove("notification_$id").apply()
    }
    
    private fun getState(id: String): NotificationState? {
        // Load from SharedPreferences or Room database
        return loadState(id)
    }
}
```

On iOS, implement similar logic using `UserDefaults` or Core Data:

```swift
class NotificationStateManager {
    private let defaults = UserDefaults.standard
    
    func shouldApplyUpdate(id: String, version: Int) -> Bool {
        guard let current = getState(id: id) else { return true }
        return version > current.version
    }
    
    func recordNotification(id: String, version: Int, timestamp: Date) {
        let state = [
            "id": id,
            "version": version,
            "timestamp": timestamp.timeIntervalSince1970
        ] as [String: Any]
        defaults.set(state, forKey: "notification_\(id)")
    }
    
    func removeNotification(id: String) {
        defaults.removeObject(forKey: "notification_\(id)")
    }
    
    private func getState(id: String) -> (id: String, version: Int, timestamp: Date)? {
        guard let state = defaults.dictionary(forKey: "notification_\(id)"),
              let version = state["version"] as? Int,
              let timestamp = state["timestamp"] as? TimeInterval else {
            return nil
        }
        return (id, version, Date(timeIntervalSince1970: timestamp))
    }
}
```

### Backend design for cross-device synchronization

Your backend needs to track which devices belong to which users and broadcast notification lifecycle events to all of a user's devices. The architecture:

```
User Action (Device A) → Backend → FCM Topic/Device Group → All User Devices
                                                          ↳ Device A (originator)
                                                          ↳ Device B
                                                          ↳ Device C
```

**Option 1: FCM Device Groups** (recommended for small user bases)

FCM Device Groups allow you to send a message to all devices associated with a notification key:

```javascript
// Backend: When user logs in, add device to their group
const deviceGroupKey = await admin.messaging().createDeviceGroup(
  `user-${userId}-devices`,
  [deviceToken]
);

// Store deviceGroupKey in your user database

// When Device A dismisses a notification, send to group
await admin.messaging().sendMulticast({
  data: {
    action: 'CANCEL_NOTIFICATION',
    notificationId: 'order-123-status',
    reason: 'dismissed_on_other_device',
    timestamp: new Date().toISOString()
  },
  tokens: deviceGroupTokens // All user's devices
});
```

**Option 2: FCM Topics** (recommended for larger scale)

Each user gets a private topic (`user-${userId}-notifications`). All their devices subscribe:

```typescript
// App: On login, subscribe to user's private topic
await FCMMessaging.subscribeToTopic({ topic: `user-${userId}-notifications` });

// Backend: Send to topic instead of individual devices
await admin.messaging().send({
  data: {
    action: 'CANCEL_NOTIFICATION',
    notificationId: 'order-123-status',
    timestamp: new Date().toISOString()
  },
  topic: `user-${userId}-notifications`
});
```

### Preventing echo: don't cancel the originating action

When a user dismisses a notification on Device A, you want to cancel it on Device B but **not re-cancel it on Device A** (which would be redundant and could cause visual glitches). Include the originating device ID in cancellation messages:

```json
{
  "data": {
    "action": "CANCEL_NOTIFICATION",
    "notificationId": "order-123-status",
    "originDeviceId": "device-abc-123",
    "timestamp": "2026-02-11T14:35:00Z"
  }
}
```

Each device checks if it's the originator:

```kotlin
"CANCEL_NOTIFICATION" -> {
    val originDevice = data["originDeviceId"]
    val thisDevice = getDeviceId() // Stored during registration
    
    if (originDevice != thisDevice) {
        // Only cancel if this isn't the device that initiated the dismissal
        notificationManager.cancel(data["notificationId"], 0)
        stateManager.removeNotification(data["notificationId"]!!)
    }
}
```

### User-initiated dismissals: sending updates to backend

When a user dismisses or interacts with a notification, send an event to your backend so it can broadcast to other devices:

```typescript
// App code: User taps notification action
NotificationRenderer.addListener('notificationAction', async (event) => {
  if (event.actionId === 'dismiss' || event.actionId === 'mark_read') {
    // Tell backend this notification was dismissed
    await fetch('https://api.yourapp.com/notifications/dismiss', {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${userToken}` },
      body: JSON.stringify({
        notificationId: event.notificationId,
        deviceId: await getDeviceId(),
        timestamp: new Date().toISOString()
      })
    });
  }
});
```

Backend endpoint:

```javascript
app.post('/notifications/dismiss', async (req, res) => {
  const { notificationId, deviceId, timestamp } = req.body;
  const userId = req.user.id;
  
  // Send cancellation to all user's other devices
  await admin.messaging().send({
    data: {
      action: 'CANCEL_NOTIFICATION',
      notificationId,
      originDeviceId: deviceId,
      timestamp
    },
    topic: `user-${userId}-notifications`
  });
  
  res.json({ success: true });
});
```

### Handling background processing: progressive updates

For long-running tasks (file uploads, order processing), send periodic update messages that refresh the notification's progress state:

```javascript
// Backend: Processing job sends updates every 5 seconds
async function processOrder(orderId, userId) {
  for (let progress = 0; progress <= 100; progress += 10) {
    await performProcessingStep();
    
    await admin.messaging().send({
      data: {
        action: 'UPDATE_NOTIFICATION',
        notificationId: `order-${orderId}-status`,
        version: String(progress / 10 + 1),
        title: 'Processing Order',
        body: `${progress}% complete`,
        style: 'progress',
        progress: String(progress),
        timestamp: new Date().toISOString()
      },
      topic: `user-${userId}-notifications`
    });
    
    await sleep(5000);
  }
  
  // Final update: completion
  await admin.messaging().send({
    data: {
      action: 'UPDATE_NOTIFICATION',
      notificationId: `order-${orderId}-status`,
      version: '11',
      title: 'Order Complete',
      body: 'Your order has been processed',
      style: 'basic',
      timestamp: new Date().toISOString()
    },
    topic: `user-${userId}-notifications`
  });
}
```

The versioning ensures that if messages arrive out of order (v10 before v9), the device applies only newer versions.

### Live Activities: remote start and update

iOS Live Activities have first-class support for remote updates via push notifications. When implementing Live Activity remote updates, use ActivityKit's push token system:

```swift
// iOS: Start a Live Activity and get its push token
let activity = try Activity.request(
    attributes: OrderAttributes(orderId: "123"),
    contentState: OrderStatus(progress: 0)
)

// Monitor for push token
for await pushToken in activity.pushTokenUpdates {
    let tokenString = pushToken.map { String(format: "%02x", $0) }.joined()
    // Send to backend
    await sendActivityTokenToBackend(tokenString, activityId: activity.id)
}
```

Backend sends updates to ActivityKit's push endpoint:

```javascript
// Backend: Update Live Activity
const apnsToken = 'device-specific-activity-push-token';
await sendActivityPushNotification(apnsToken, {
  event: 'update',
  contentState: {
    progress: 75,
    status: 'Almost done'
  },
  timestamp: Date.now()
});
```

### TypeScript API additions for remote management

Extend your plugin interface to support these patterns:

```typescript
export interface NotificationRendererPlugin {
  // ...existing methods...
  
  // Get current device's unique ID for echo prevention
  getDeviceId(): Promise<{ deviceId: string }>;
  
  // Get all currently displayed notifications
  getActiveNotifications(): Promise<{ notifications: NotificationInfo[] }>;
  
  // Programmatically update a notification (for backend-driven updates)
  updateNotification(options: UpdateNotificationOptions): Promise<void>;
  
  // Programmatically cancel a notification (for remote dismissals)
  cancelNotification(options: { id: string; reason?: string }): Promise<void>;
  
  // Listen for user dismissals to send to backend
  addListener(
    event: 'notificationDismissed',
    fn: (data: { notificationId: string; timestamp: number }) => void
  ): Promise<PluginListenerHandle>;
}

export interface NotificationInfo {
  id: string;
  title: string;
  body: string;
  version: number;
  timestamp: number;
}

export interface UpdateNotificationOptions {
  id: string;
  version?: number;  // For conflict resolution
  title?: string;
  body?: string;
  progress?: number;
  // ...other properties...
}
```

### Conflict resolution strategies

When multiple devices or the backend send conflicting updates simultaneously, use these strategies:

**Last-write-wins with version numbers:** Higher version always wins (as shown above)

**Timestamp-based:** If versions are equal, newer timestamp wins

```kotlin
fun shouldApplyUpdate(id: String, version: Int, timestamp: Long): Boolean {
    val current = getState(id) ?: return true
    return when {
        version > current.version -> true
        version < current.version -> false
        else -> timestamp > current.timestamp // Version equal, use timestamp
    }
}
```

**Tombstone pattern:** When a notification is cancelled, store a tombstone entry for 24 hours to prevent resurrection by stale messages:

```kotlin
fun removeNotification(id: String) {
    val tombstone = NotificationState(
        id = id,
        version = Int.MAX_VALUE,  // No future update can exceed this
        timestamp = System.currentTimeMillis()
    )
    saveState(id, tombstone)
    
    // Schedule cleanup after 24 hours
    scheduleTombstoneCleanup(id, 24.hours)
}
```

### Testing remote synchronization

Testing cross-device behavior requires simulating multi-device scenarios:

```kotlin
@Test
fun `stale update is rejected when newer version exists`() {
    val manager = NotificationStateManager(mockPrefs)
    
    // Device receives v2 update
    manager.recordNotification("test-id", version = 2, timestamp = 100L)
    
    // Device receives delayed v1 update
    assertFalse(manager.shouldApplyUpdate("test-id", version = 1))
}

@Test
fun `concurrent updates with same version use timestamp`() {
    val manager = NotificationStateManager(mockPrefs)
    
    manager.recordNotification("test-id", version = 1, timestamp = 100L)
    
    // Newer timestamp wins
    assertTrue(manager.shouldApplyUpdate("test-id", version = 1))
}
```

For integration testing, use Firebase Test Lab with multiple emulators subscribing to the same FCM topic, then verify that actions on one emulator propagate to others.

### Summary: Remote management architecture

The complete flow for a user dismissing a notification on one device:

1. User swipes notification on Device A
2. App catches dismissal event, calls backend API with `notificationId` and `deviceId`
3. Backend sends FCM data message with `action: 'CANCEL_NOTIFICATION'` to user's topic
4. All user devices receive message (A, B, C)
5. Each device checks if it's the originator (`originDeviceId != thisDeviceId`)
6. Devices B and C cancel their local notifications; Device A ignores (already dismissed)
7. All devices update their state managers to record cancellation (prevents resurrection)

This architecture makes notification state eventually consistent across all user devices while handling network delays, out-of-order delivery, and conflicting updates gracefully.

## Local notifications: shared rendering, separate scheduling

An important architectural question: should your notification renderer plugin also handle **local notifications** (notifications scheduled and triggered entirely on-device without any server involvement)? The answer is **yes, absolutely** — and the two-plugin architecture you've built actually makes this natural.

### Why local notifications belong in the same renderer plugin

Local and remote notifications share 90% of their implementation:

**Shared concerns (same code):**
- Notification display APIs (`NotificationCompat` on Android, `UNUserNotificationCenter` on iOS)
- All rendering styles (expandable, progress, call, Live Activities)
- Channel management (Android)
- Notification categories and actions (iOS)
- Permission handling
- User interaction callbacks
- The entire `NotificationFactory` and builder classes

**Different concerns (separate code):**
- **Scheduling mechanism** — local notifications need timers/triggers, remote notifications arrive via FCM
- **Payload source** — local notifications read from device storage, remote notifications arrive in FCM data messages
- **Cancellation scope** — local notifications cancel scheduled future notifications, remote notifications cancel displayed notifications

The key insight: **your NotificationFactory doesn't care whether a notification payload came from FCM or local storage**. It just builds and displays notifications. This means local notification support is primarily a *scheduling layer* on top of your existing rendering infrastructure.

### Extending the TypeScript API for local notifications

Add local notification methods to your `NotificationRendererPlugin`:

```typescript
export interface NotificationRendererPlugin {
  // ...existing remote notification methods...
  
  // Local notification scheduling
  schedule(options: ScheduleNotificationOptions): Promise<{ id: string }>;
  cancelScheduled(options: { id: string }): Promise<void>;
  getPendingNotifications(): Promise<{ notifications: PendingNotification[] }>;
  
  // Periodic/repeating notifications
  scheduleRepeating(options: RepeatingNotificationOptions): Promise<{ id: string }>;
}

export interface ScheduleNotificationOptions extends NotificationOptions {
  // Trigger options
  at?: Date;                    // Trigger at specific time
  in?: number;                  // Trigger after N milliseconds
  every?: 'minute' | 'hour' | 'day' | 'week' | 'month';  // Repeating
  
  // Platform-specific triggers
  android?: {
    exactTiming?: boolean;      // Use AlarmManager for exact timing
    allowWhileIdle?: boolean;   // Fire even in Doze mode
  };
  ios?: {
    repeats?: boolean;
    timeInterval?: number;      // UNTimeIntervalNotificationTrigger
    dateComponents?: {          // UNCalendarNotificationTrigger
      hour?: number;
      minute?: number;
      weekday?: number;
    };
  };
}

export interface PendingNotification {
  id: string;
  scheduledFor: Date;
  options: NotificationOptions;
}
```

### Android implementation: WorkManager vs AlarmManager

On Android, you have two main scheduling approaches, each with different trade-offs:

**Option 1: WorkManager (recommended for most cases)**

WorkManager provides robust, battery-efficient scheduling that survives app restarts and device reboots:

```kotlin
class LocalNotificationScheduler(private val context: Context) {
    private val factory = NotificationFactory()
    
    fun schedule(options: ScheduleNotificationOptions) {
        val delay = options.scheduledTime - System.currentTimeMillis()
        
        val workRequest = OneTimeWorkRequestBuilder<NotificationWorker>()
            .setInitialDelay(delay, TimeUnit.MILLISECONDS)
            .setInputData(workDataOf(
                "notification_data" to options.toJson()
            ))
            .build()
        
        WorkManager.getInstance(context)
            .enqueueUniqueWork(
                options.id,
                ExistingWorkPolicy.REPLACE,
                workRequest
            )
    }
    
    fun cancel(id: String) {
        WorkManager.getInstance(context).cancelUniqueWork(id)
    }
}

class NotificationWorker(
    context: Context,
    params: WorkerParameters
) : Worker(context, params) {
    
    override fun doWork(): Result {
        val notificationData = inputData.getString("notification_data")
        val options = NotificationOptions.fromJson(notificationData)
        
        // Use the same NotificationFactory as remote notifications
        val factory = NotificationFactory()
        val notification = factory.build(applicationContext, options)
        
        NotificationManagerCompat.from(applicationContext)
            .notify(options.id.hashCode(), notification)
        
        return Result.success()
    }
}
```

**WorkManager benefits:**
- Battery-efficient (uses JobScheduler under the hood)
- Survives app kills and device reboots automatically
- Handles Doze mode constraints
- Built-in retry logic

**WorkManager limitations:**
- Not exact — timing can be off by several minutes
- Minimum delay is ~15 minutes in practice
- Not suitable for time-sensitive notifications (alarms, reminders with specific times)

**Option 2: AlarmManager (for exact timing)**

When you need precise timing (alarm clock, medication reminders, meeting alerts), use `AlarmManager`:

```kotlin
class ExactNotificationScheduler(private val context: Context) {
    
    fun scheduleExact(options: ScheduleNotificationOptions) {
        val alarmManager = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        
        val intent = Intent(context, NotificationReceiver::class.java).apply {
            action = "SHOW_NOTIFICATION"
            putExtra("notification_data", options.toJson())
        }
        
        val pendingIntent = PendingIntent.getBroadcast(
            context,
            options.id.hashCode(),
            intent,
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        
        // Android 12+ requires SCHEDULE_EXACT_ALARM permission
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            if (alarmManager.canScheduleExactAlarms()) {
                alarmManager.setExactAndAllowWhileIdle(
                    AlarmManager.RTC_WAKEUP,
                    options.scheduledTime,
                    pendingIntent
                )
            } else {
                // Fallback to inexact alarm or request permission
                scheduleInexact(alarmManager, options, pendingIntent)
            }
        } else {
            alarmManager.setExactAndAllowWhileIdle(
                AlarmManager.RTC_WAKEUP,
                options.scheduledTime,
                pendingIntent
            )
        }
    }
}

class NotificationReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val notificationData = intent.getStringExtra("notification_data")
        val options = NotificationOptions.fromJson(notificationData)
        
        // Same NotificationFactory again
        val factory = NotificationFactory()
        val notification = factory.build(context, options)
        
        NotificationManagerCompat.from(context)
            .notify(options.id.hashCode(), notification)
    }
}
```

**AlarmManager critical considerations:**
- **Android 12+ requires `SCHEDULE_EXACT_ALARM` permission** — this is a user-facing permission that must be requested explicitly
- **Android 14+ restricts this further** — only alarm clocks and calendar apps get exact alarms by default
- Must survive device reboots (requires `RECEIVE_BOOT_COMPLETED` permission and boot receiver)
- Must handle app updates (scheduled alarms are cleared on app update)

**Hybrid approach (recommended):**

```kotlin
fun schedule(options: ScheduleNotificationOptions) {
    val needsExactTiming = options.android?.exactTiming == true
    
    if (needsExactTiming) {
        // Check if we can use exact alarms
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val alarmManager = context.getSystemService(AlarmManager::class.java)
            if (alarmManager.canScheduleExactAlarms()) {
                scheduleExact(options)
            } else {
                // Fall back to WorkManager or throw error asking for permission
                scheduleWithWorkManager(options)
            }
        } else {
            scheduleExact(options)
        }
    } else {
        scheduleWithWorkManager(options)
    }
}
```

### iOS implementation: UNNotificationRequest triggers

iOS local notifications use the same `UNUserNotificationCenter` as remote notifications, but with triggers:

```swift
class LocalNotificationScheduler {
    private let center = UNUserNotificationCenter.current()
    private let factory = NotificationFactory()
    
    func schedule(options: ScheduleNotificationOptions) throws {
        let content = factory.buildContent(from: options)
        
        // Create trigger based on scheduling options
        let trigger: UNNotificationTrigger
        
        if let timeInterval = options.ios?.timeInterval {
            trigger = UNTimeIntervalNotificationTrigger(
                timeInterval: timeInterval,
                repeats: options.ios?.repeats ?? false
            )
        } else if let dateComponents = options.ios?.dateComponents {
            trigger = UNCalendarNotificationTrigger(
                dateMatching: dateComponents,
                repeats: options.ios?.repeats ?? false
            )
        } else if let scheduledDate = options.at {
            let timeInterval = scheduledDate.timeIntervalSinceNow
            trigger = UNTimeIntervalNotificationTrigger(
                timeInterval: max(timeInterval, 1),
                repeats: false
            )
        } else {
            throw SchedulingError.noTriggerSpecified
        }
        
        let request = UNNotificationRequest(
            identifier: options.id,
            content: content,
            trigger: trigger
        )
        
        center.add(request) { error in
            if let error = error {
                print("Scheduling failed: \(error)")
            }
        }
    }
    
    func cancel(id: String) {
        center.removePendingNotificationRequests(withIdentifiers: [id])
    }
    
    func getPending(completion: @escaping ([UNNotificationRequest]) -> Void) {
        center.getPendingNotificationRequests(completionHandler: completion)
    }
}
```

**iOS local notification considerations:**
- Much simpler than Android — no WorkManager/AlarmManager complexity
- Triggers are reliable and exact (no "inexact timing" concept)
- Automatically survive app kills and device reboots
- Maximum of **64 pending notifications per app** — after that, oldest are silently dropped
- Repeating notifications require careful trigger setup:
  - `UNTimeIntervalNotificationTrigger` with `repeats: true` requires `timeInterval >= 60` (minimum 1 minute)
  - `UNCalendarNotificationTrigger` with `repeats: true` fires at the same time each day/week/month

### Critical: Boot receivers and persistence

Both platforms require handling device reboots to restore scheduled notifications:

**Android: Boot receiver**

```kotlin
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED) {
            // Restore all scheduled notifications from storage
            val storage = NotificationStorage(context)
            val pending = storage.getAllPending()
            
            val scheduler = LocalNotificationScheduler(context)
            pending.forEach { notification ->
                scheduler.schedule(notification)
            }
        }
    }
}

// AndroidManifest.xml
<receiver android:name=".BootReceiver"
          android:enabled="true"
          android:exported="true">
    <intent-filter>
        <action android:name="android.intent.action.BOOT_COMPLETED"/>
    </intent-filter>
</receiver>

<uses-permission android:name="android.permission.RECEIVE_BOOT_COMPLETED"/>
```

**iOS: Automatic persistence**

iOS automatically persists pending notification requests across reboots — you don't need to implement anything. However, you should provide a way for users to query pending notifications:

```swift
func getPendingNotifications() async -> [PendingNotificationInfo] {
    let requests = await center.pendingNotificationRequests()
    return requests.map { request in
        PendingNotificationInfo(
            id: request.identifier,
            scheduledFor: request.trigger?.nextTriggerDate()
        )
    }
}
```

### Storage layer for pending notifications

You need local storage to track scheduled notifications, especially for:
- Restoring after Android reboot
- Querying pending notifications from JavaScript
- Handling app updates

```kotlin
class NotificationStorage(context: Context) {
    private val prefs = context.getSharedPreferences("pending_notifications", Context.MODE_PRIVATE)
    
    fun savePending(id: String, options: ScheduleNotificationOptions) {
        val json = options.toJson()
        prefs.edit().putString(id, json).apply()
    }
    
    fun removePending(id: String) {
        prefs.edit().remove(id).apply()
    }
    
    fun getAllPending(): List<ScheduleNotificationOptions> {
        return prefs.all.values.mapNotNull { value ->
            (value as? String)?.let { ScheduleNotificationOptions.fromJson(it) }
        }
    }
}
```

### When to use local vs remote notifications

**Use local notifications for:**
- User-scheduled reminders (tasks, medications, appointments)
- Recurring alarms or timers
- Time-based content unlocks (game energy refills)
- Offline functionality (the app must work without internet)

**Use remote (FCM) notifications for:**
- Events triggered by other users (messages, mentions, follows)
- Server-side state changes (order shipped, payment processed)
- Breaking news or time-sensitive announcements
- Cross-device synchronization (notification on phone triggers update on tablet)

**Use both together for:**
- Calendar apps — remote notification when someone invites you to an event, local notification when the event starts
- Fitness apps — remote notification for social challenges, local notification for daily workout reminders
- E-commerce — remote notification when price drops, local notification for abandoned cart

### Maintenance implications

Adding local notifications to your plugin **increases maintenance burden slightly** but not dramatically:

**Additional OS version concerns:**
- Android 12+ exact alarm permissions (already covered above)
- Android 14 foreground service type restrictions affect alarms
- iOS 64-notification limit (static, won't change)

**Additional testing:**
- Device reboot scenarios
- App update scenarios (AlarmManager clears on update)
- Doze mode behavior (Android)
- Time zone changes
- System date/time changes

**The good news:** Your modular architecture absorbs this well. Local notification scheduling is a **separate concern** from rendering. You can add a `LocalNotificationScheduler` class alongside your `NotificationFactory`, and they coexist without coupling.

### Summary: Should you include local notifications?

**Yes, for three reasons:**

1. **Architectural fit** — Local and remote notifications share all rendering code. Your NotificationFactory works identically for both.

2. **User expectation** — Apps that handle push notifications almost always need local notifications too. Building two separate plugins would be confusing.

3. **Modest complexity** — The scheduling layer is relatively simple (WorkManager/AlarmManager on Android, UNNotificationRequest triggers on iOS), and it's orthogonal to the notification rendering complexity you've already tackled.

The expanded plugin becomes:
- **FCM Messaging Plugin** (unchanged) — handles FCM tokens, message delivery
- **Notification Renderer Plugin** (expanded) — handles both remote notification display AND local notification scheduling + display

This keeps the two-plugin architecture intact while providing complete notification functionality.

## Testing and CI strategies that catch OS breakage early

Native plugin testing requires a multi-layered approach because failures can occur at the TypeScript level, the bridge level, or deep in platform-specific code.

**Unit test the implementation classes, not the bridge classes.** Following Capacitor's bridge-implementation separation ([(Capacitor, 2024a)](#capacitor-2024a)), your `BasicNotificationBuilder.kt` and `LiveActivityManager.swift` should be pure native classes with no Capacitor imports. Test them with JUnit 5 + Mockito on Android and XCTest on iOS ([(Ionic, 2024)](#ionic-2024)). Mock the Android `Context` and `NotificationManager`; mock `UNUserNotificationCenter` on iOS. This gives you fast, reliable tests that run without emulators.

**For JavaScript-side testing**, Capacitor plugins are JavaScript Proxies that can't be proxied again by standard mocking libraries [(Capacitor, 2024c)](#capacitor-2024c). Use Jest manual mocks in a `__mocks__/@capacitor/` directory, or Jasmine path mapping in `tsconfig.spec.json`. Mock the plugin interface to test your app's notification logic without native dependencies.

**For cross-version compatibility**, set up CI jobs on **Firebase Test Lab** (Android) and **BrowserStack** (iOS) that run your native test suite against multiple OS versions [(BrowserStack, 2024)](#browserstack-2024). Target your minimum supported API level, the version where each major feature was introduced (API 26 for channels, API 31 for CallStyle, API 33 for permissions), and the latest stable release. On iOS, test on your minimum deployment target, iOS 15 (interruption levels), iOS 16.1 (Live Activities), and the latest version.

**When OS betas drop in June/July each year**, trigger a CI run against the beta. Both Firebase Test Lab and BrowserStack provide new OS versions on or near launch day. This gives you 3-4 months before the public release to identify and fix compatibility issues. Budget this annual beta testing sprint explicitly.

Use **semantic-release with Conventional Commits** for automated versioning and changelog generation [(Rumbaut, 2023)](#rumbaut-2023). Every commit message like `feat(android): add ProgressStyle support` or `fix(ios): handle ActivityKit token refresh on iOS 18` automatically drives version bumps and produces a changelog that helps users understand what changed and why.

## Reducing solo maintenance burden through modularity and community

The Capacitor plugin ecosystem's sustainability data is sobering. Both major Cordova FCM plugins are solo-maintained with explicit pleas for donations [(Capawesome Team, 2024)](#capawesome-2024). The original `cordova-plugin-firebase` accumulated **226 open issues** before being abandoned [(Arnesson, 2024)](#arnesson-2024). Even capawesome's Robin Genz, maintaining 30+ plugins with 500,000+ monthly downloads, describes it as a "side project" [(Capawesome Team, 2024)](#capawesome-2024). The pattern is clear: **solo maintenance of cross-platform native plugins leads to burnout and abandonment**.

Structural choices that reduce this burden:

**Modular notification types mean independent contributions.** If each notification style (expandable, progress, call, Live Activity) lives in its own file with a clear interface, contributors can add or fix a single style without understanding the entire plugin. This is the single most important decision for community sustainability [(Capgo, 2024a)](#capgo-2024a).

**Align major versions with Capacitor majors** (as all successful plugins do). Capacitor ships migration CLI tools (`@capacitor/plugin-migration-vX-to-vY`) that handle most boilerplate updates ([(Capacitor, 2024b)](#capacitor-2024b)). The core bridge communication pattern — `registerPlugin()`, `call.resolve()`/`call.reject()`, `notifyListeners()` — has been stable since Capacitor 3, so your bridge classes will need minimal changes.

**Keep the NotificationCompat / AndroidX dependency current.** Google continuously backports new notification features to NotificationCompat (CallStyle was added after Android 12 shipped; ProgressStyle was added for Android 16) [(Wix, 2019)](#wix-2019). Simply updating your AndroidX dependency often adds new capability support with zero code changes.

**Consider Kotlin Multiplatform (KMP) for shared business logic** like notification payload parsing, scheduling logic, and capability detection. KMP compiles to both JVM (Android) and Native (iOS), eliminating duplicated logic. Mozilla's UniFFI-rs offers a similar approach using Rust as the shared language, with auto-generated Swift and Kotlin bindings that "feel idiomatic" ([(Mozilla, 2020;](#mozilla-2020) [(Stadia Maps, 2024)](#stadia-2024)). Either approach reduces the surface area where platform-divergent bugs can hide.

**Publish the plugin as open-source with clear contribution guidelines**, a CONTRIBUTING.md, and architectural decision records (ADRs) explaining why each design choice was made [(Capgo, 2024b)](#capgo-2024b). The modular structure means drive-by contributors can fix or add a single notification type without deep architecture knowledge.

## Conclusion

The sustainable path is not a single monolithic plugin but a **layered architecture**: use an existing plugin for FCM messaging, build a modular rendering plugin with isolated per-style builders, and gate every platform-specific feature behind runtime capability detection. This structure means Android 17's new notification features become a new builder class, iOS 19's ActivityKit changes stay contained in `LiveActivityManager.swift`, and Capacitor 9's migration affects only the thin bridge classes.

Three decisions matter most for long-term maintainability: **data-only FCM messages** (eliminating the background notification display problem entirely), **NotificationCompat as your Android abstraction layer** (letting Google handle version compatibility), and **complete isolation of ActivityKit code** (your single highest-churn dependency). Everything else — testing strategy, versioning, community structure — amplifies these core architectural choices. Budget for two focused maintenance sprints per year: one when OS betas drop (June), one when they ship (September-October). With this architecture, each sprint should require days of work, not weeks.

---

## References

<a id="android-2024a"></a>Android Developers. (2024a). Behavior changes: Apps targeting Android 12. https://developer.android.com/about/versions/12/behavior-changes-12

<a id="android-2024b"></a>Android Developers. (2024b). Behavior changes: Apps targeting Android 14 or higher. https://developer.android.com/about/versions/14/behavior-changes-14

<a id="android-2024c"></a>Android Developers. (2024c). Create a notification. https://developer.android.com/develop/ui/views/notifications/build-notification

<a id="android-2024d"></a>Android Developers. (2024d). Create a call style notification for call apps. https://developer.android.com/develop/ui/views/notifications/call-style

<a id="android-2024e"></a>Android Developers. (2024e). Foreground service types are required. https://developer.android.com/about/versions/14/changes/fgs-types-required

<a id="apple-2021"></a>Apple. (2021). Send communication and time sensitive notifications [Video]. WWDC21. https://developer.apple.com/videos/play/wwdc2021/10091/

<a id="apple-forums-2024"></a>Apple Developer Forums. (2024). Live Activities push-to-start flows. https://developer.apple.com/forums/thread/805324

<a id="arnesson-2024"></a>Arnesson. (2024). cordova-plugin-firebase [GitHub repository]. GitHub. https://github.com/arnesson/cordova-plugin-firebase/issues

<a id="batch-2022"></a>Batch. (2022). WWDC news for push notifications for iOS16 and macOS Ventura. https://batch.com/blog/posts/major-announcements-push-notifications-ios-16-macos-ventura-wwdc-2022

<a id="browserstack-2024"></a>BrowserStack. (2024). Automated app testing on real mobile devices. https://www.browserstack.com/app-automate

<a id="capawesome-2024"></a>Capawesome Team. (2024). @capacitor-firebase/messaging [npm package]. https://github.com/capawesome-team/capacitor-firebase/blob/main/packages/messaging/README.md

<a id="capacitor-2024a"></a>Capacitor. (2024a). Building a Capacitor plugin: Code abstraction patterns. https://capacitorjs.com/docs/plugins/tutorial/code-abstraction-patterns

<a id="capacitor-2024b"></a>Capacitor. (2024b). Updating plugins to 7.0. https://capacitorjs.com/docs/updating/plugins/7-0

<a id="capacitor-2024c"></a>Capacitor. (2024c). Mocking plugins. https://capacitorjs.com/docs/guides/mocking-plugins

<a id="capacitor-community-2024"></a>Capacitor Community. (2024). fcm: Enable Firebase Cloud Messaging for Capacitor apps [GitHub repository]. https://github.com/capacitor-community/fcm

<a id="capgo-2024a"></a>Capgo. (2024a). Ultimate guide to Capacitor plugin development. https://capgo.app/blog/ultimate-guide-to-capacitor-plugin-development/

<a id="capgo-2024b"></a>Capgo. (2024b). Capacitor plugin contribution guide. https://capgo.app/blog/capacitor-plugin-contribution-guide/

<a id="expertappdevs-2024"></a>Expert App Devs. (2024). Android 16: A quietly powerful update that changes more than you think. Medium. https://medium.com/@expertappdevs/android-16-a-quietly-powerful-update-that-changes-more-than-you-think-ae53726ef2d3

<a id="firebase-2024a"></a>Firebase. (2024a). Receive messages in Android apps. https://firebase.google.com/docs/cloud-messaging/android/receive-messages

<a id="firebase-2024b"></a>Firebase. (2024b). Get started with Firebase Cloud Messaging in Apple platform apps. https://firebase.google.com/docs/cloud-messaging/ios/get-started

<a id="google-2024"></a>Google. (2024). Understanding foreground service and full-screen intent requirements. Play Console Help. https://support.google.com/googleplay/android-developer/answer/13392821

<a id="harkhani-2024"></a>Harkhani, G. (2024). Mastering Live Activities in iOS: The complete developer's guide. Medium. https://medium.com/@gauravharkhani01/mastering-live-activities-in-ios-the-complete-developers-guide-5357eb35d520

<a id="homeassistant-2021"></a>Home Assistant. (2021). [iOS 15] Add entitlement and notification settings for time sensitive notifications [Issue #1659]. GitHub. https://github.com/home-assistant/iOS/issues/1659

<a id="ionic-2024"></a>Ionic. (2024). Unit testing Capacitor plugins. Ionic Enterprise Tutorials. https://ionic.io/docs/tutorials/custom-plugins/unit-testing/android

<a id="ionic-2021"></a>Ionic Team. (2021). feat: Add progress bar to local notification plugin [Issue #361]. GitHub. https://github.com/ionic-team/capacitor-plugins/issues/361

<a id="ionic-2024a"></a>Ionic Team. (2024a). Capacitor plugins: Official plugins for Capacitor [GitHub repository]. https://github.com/ionic-team/capacitor-plugins

<a id="ionic-2024b"></a>Ionic Team. (2024b). Push notifications Capacitor plugin API. https://capacitorjs.com/docs/apis/push-notifications

<a id="mozilla-2020"></a>Mozilla. (2020). This week in Glean: Cross-platform language binding generation with Rust and "uniffi". Data@Mozilla. https://blog.mozilla.org/data/2020/10/21/this-week-in-glean-cross-platform-language-binding-generation-with-rust-and-uniffi/

<a id="notifyvisitors-2024"></a>NotifyVisitors. (2024). Notification service extension (Capacitor). https://docs.notifyvisitors.com/docs/capacitor-notification-service-extension

<a id="pushwoosh-2024"></a>Pushwoosh. (2024). iOS push notifications guide (2026): How they work, setup, and best practices. https://www.pushwoosh.com/blog/ios-push-notifications/

<a id="rumbaut-2023"></a>Rumbaut, G. (2023). Releases the easy way. Medium. https://medium.com/@gabrielrumbaut/releases-the-easy-way-3ec1c2c3502b

<a id="sqlpey-2024"></a>Sqlpey. (2024). FCM data vs notification: Handling background messages in Android. https://www.sqlpey.com/firebase/fcm-background-messages-android/

<a id="stadia-2024"></a>Stadia Maps. (2024). Ferrostar: Building a cross-platform navigation SDK in Rust (Part 1). https://stadiamaps.com/news/ferrostar-building-a-cross-platform-navigation-sdk-in-rust-part-1/

<a id="wix-2019"></a>Wix. (2019). Replace deprecated notification util with NotificationCompat [Issue #574]. GitHub. https://github.com/wix/react-native-notifications/issues/574

<a id="xamarin-2024"></a>Xamarin. (2024). Firebase Cloud Messaging on iOS. GitHub. https://github.com/xamarin/GoogleApisForiOSComponents/blob/main/docs/Firebase/CloudMessaging/GettingStarted.md
