# temporary_app

## Note:

```cmd
ionic build

npx cap sync android
cd android
./gradlew clean
./gradlew build

cd ..
npx cap open android
```


```
# 1. Build your web code
npm run build

# 2. Copy to Android project
npx cap copy android

# 3. Sync Capacitor plugins
npx cap sync android

# 4. Clean Android build
cd android
./gradlew clean
./gradlew build
```

```
cd ..
npx cap run android

```

This is a **very common Capacitor “stuck with old package” problem**. Even though you changed your **`capacitor.config.ts`** and `build.gradle`, Android is still trying to use `io.ionic.starter` because **Capacitor’s Android project was initially generated with that old ID**.

So you need to **fully update the Android project to the new app ID / namespace**.

Here’s the proper way to fix it:

---

## **1. Update Capacitor config (you already did)**

```ts
// capacitor.config.ts
import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.voortman.insights', // <-- new ID
  appName: 'tempApp',
  webDir: 'www'
};

export default config;
```

* ✅ This is correct. But Capacitor **does not automatically change the native Android project** if it already exists.

---

## **2. Remove the old Android project (optional but clean)**

If this is a test project and you don’t have native customizations, the easiest way is to **recreate the Android folder**:

```bash
npx cap add android
```

* This generates a fresh Android project **with the new `appId` and namespace**.
* Now your `android/app/src/main/java/com/voortman/insights/MainActivity.java` will match your new ID.

> ⚠ Important: If you have custom native changes (plugins, etc.), back them up first.

---

## **3. Update Android build.gradle**

In `android/app/build.gradle`:

```gradle
android {
    namespace = "com.voortman.insights"  // matches MainActivity package
    defaultConfig {
        applicationId "com.voortman.insights"  // matches Capacitor config & Firebase
        minSdkVersion 21
        targetSdkVersion 33
        versionCode 1
        versionName "1.0.0"
    }
}
```

* **Both namespace and applicationId** must match the new ID.
* Remove any references to `io.ionic.starter` (old default).

---

## **4. Update MainActivity.java**

`android/app/src/main/java/com/voortman/insights/MainActivity.java`:

```java
package com.voortman.insights;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {}
```

* The `package` **must match `namespace`**.

---

## **5. Copy / Sync Capacitor plugins**

After changing JS/TS code or adding Firebase:

```bash
npm run build
npx cap copy android
npx cap sync android
```

---

## **6. Clean & Rebuild in Android Studio**

```bash
cd android
./gradlew clean
./gradlew build
```

