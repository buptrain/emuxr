package com.tiger.gbaixr;

import android.app.Activity;
import android.content.Intent;

import java.lang.reflect.Field;
import java.lang.reflect.Method;

/**
 * Unity entry point for a one-time, app-private ROM import. Reflection avoids
 * a direct unityLibrary dependency from this Android Library module.
 */
public final class RomImportBridge {
    private RomImportBridge() {
    }

    public static void requestImport(final String receiverObject, final String callbackMethod) {
        final Activity activity = currentActivity();
        if (activity == null) {
            sendMessage(receiverObject, callbackMethod,
                    RomImportActivity.errorPayload("Android activity is unavailable."));
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                Intent intent = new Intent(activity, RomImportActivity.class);
                intent.putExtra(RomImportActivity.EXTRA_RECEIVER_OBJECT, receiverObject);
                intent.putExtra(RomImportActivity.EXTRA_CALLBACK_METHOD, callbackMethod);
                activity.startActivity(intent);
            }
        });
    }

    static void sendMessage(String receiverObject, String callbackMethod, String payload) {
        try {
            Class<?> unityPlayer = Class.forName("com.unity3d.player.UnityPlayer");
            Method unitySendMessage = unityPlayer.getMethod(
                    "UnitySendMessage", String.class, String.class, String.class);
            unitySendMessage.invoke(null, receiverObject, callbackMethod, payload);
        } catch (Exception ignored) {
            // There is no safe in-app fallback if Unity is already unavailable.
        }
    }

    private static Activity currentActivity() {
        try {
            Class<?> unityPlayer = Class.forName("com.unity3d.player.UnityPlayer");
            Field currentActivity = unityPlayer.getField("currentActivity");
            Object activity = currentActivity.get(null);
            return activity instanceof Activity ? (Activity) activity : null;
        } catch (Exception ignored) {
            return null;
        }
    }
}
