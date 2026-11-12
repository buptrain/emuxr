package io.github.buptrain.emuxr.romimport;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.security.MessageDigest;
import java.util.Locale;

/**
 * Private picker activity. ACTION_OPEN_DOCUMENT exposes the full system file
 * browser; the selected stream is copied into Unity's app-specific
 * external-files/roms directory and no external URI permission is retained.
 * This does not depend on UnityPlayerActivity or GameActivity activity-result
 * forwarding.
 */
public final class RomImportActivity extends Activity {
    static final String EXTRA_RECEIVER_OBJECT = "receiverObject";
    static final String EXTRA_CALLBACK_METHOD = "callbackMethod";
    private static final int REQUEST_PICK_ROM = 6241;
    private static final long MAX_ROM_BYTES = 32L * 1024L * 1024L;

    private String receiverObject;
    private String callbackMethod;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        receiverObject = getIntent().getStringExtra(EXTRA_RECEIVER_OBJECT);
        callbackMethod = getIntent().getStringExtra(EXTRA_CALLBACK_METHOD);
        if (receiverObject == null || callbackMethod == null) {
            finish();
            return;
        }

        // Some Android XR GET_CONTENT providers expose only indexed media and
        // hide valid ADB-copied .gba files. OPEN_DOCUMENT uses DocumentsUI's
        // full document browser. We do not call takePersistableUriPermission:
        // the URI is consumed only for this immediate app-private copy.
        Intent pickerIntent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        pickerIntent.addCategory(Intent.CATEGORY_OPENABLE);
        // GBA ROMs do not have a consistently registered MIME type across
        // Android document providers. Restricting EXTRA_MIME_TYPES can make
        // valid .gba files invisible in DocumentsUI. The import path below
        // remains authoritative: it validates the extension and bounded copy.
        pickerIntent.setType("*/*");
        pickerIntent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        startActivityForResult(Intent.createChooser(pickerIntent, "Select a Game Boy Advance ROM"), REQUEST_PICK_ROM);
    }

    @Override
    @SuppressWarnings("deprecation")
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_PICK_ROM) {
            return;
        }
        if (resultCode != RESULT_OK || data == null || data.getData() == null) {
            sendAndFinish("{\"status\":\"cancelled\"}");
            return;
        }

        final Uri uri = data.getData();
        new Thread(new Runnable() {
            @Override
            public void run() {
                importUri(uri);
            }
        }, "GbaXrRomImport").start();
    }

    private void importUri(Uri uri) {
        String originalFileName = queryDisplayName(uri);
        if (originalFileName == null || !originalFileName.toLowerCase(Locale.ROOT).endsWith(".gba")) {
            sendAndFinish(errorPayload("Select a .gba ROM file."));
            return;
        }

        // Unity's Android persistentDataPath maps to the app-specific external
        // files directory. Use the same root so managed catalog validation can
        // resolve the returned relative path without exposing broad storage.
        File appPrivateRoot = getExternalFilesDir(null);
        if (appPrivateRoot == null) {
            sendAndFinish(errorPayload("App-private external storage is unavailable."));
            return;
        }
        File romDirectory = new File(appPrivateRoot, "roms");
        if (!romDirectory.exists() && !romDirectory.mkdirs()) {
            sendAndFinish(errorPayload("Could not create app-private ROM storage."));
            return;
        }

        File temporaryFile = null;
        try {
            temporaryFile = File.createTempFile("import-", ".tmp", romDirectory);
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            long sizeBytes = copyAndHash(uri, temporaryFile, digest);
            if (sizeBytes <= 0L) {
                throw new IOException("The selected ROM is empty.");
            }
            String hash = toHex(digest.digest());
            String relativePath = hash + ".gba";
            File finalFile = new File(romDirectory, relativePath);
            if (finalFile.exists()) {
                if (!temporaryFile.delete()) {
                    throw new IOException("Could not discard duplicate import temp file.");
                }
            } else if (!temporaryFile.renameTo(finalFile)) {
                throw new IOException("Could not finalize the app-private ROM copy.");
            }

            sendAndFinish(successPayload(originalFileName, relativePath, hash, sizeBytes));
        } catch (Exception exception) {
            if (temporaryFile != null && temporaryFile.exists()) {
                // Best effort only: an intact prior catalog is never changed here.
                temporaryFile.delete();
            }
            sendAndFinish(errorPayload("ROM import failed: " + exception.getMessage()));
        }
    }

    private long copyAndHash(Uri uri, File output, MessageDigest digest) throws IOException {
        long total = 0L;
        byte[] buffer = new byte[32 * 1024];
        InputStream providerStream = getContentResolver().openInputStream(uri);
        if (providerStream == null) {
            throw new IOException("The selected provider did not supply readable content.");
        }
        try {
            BufferedInputStream input = new BufferedInputStream(providerStream);
            try {
                FileOutputStream fileOutput = new FileOutputStream(output);
                try {
                    int count;
                    while ((count = input.read(buffer)) != -1) {
                        total += count;
                        if (total > MAX_ROM_BYTES) {
                            throw new IOException("GBA ROMs may not exceed 32 MiB.");
                        }
                        digest.update(buffer, 0, count);
                        fileOutput.write(buffer, 0, count);
                    }
                    fileOutput.getFD().sync();
                } finally {
                    fileOutput.close();
                }
            } finally {
                input.close();
            }
        } finally {
            providerStream.close();
        }
        return total;
    }

    private String queryDisplayName(Uri uri) {
        Cursor cursor = null;
        try {
            cursor = getContentResolver().query(uri, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null);
            if (cursor != null && cursor.moveToFirst()) {
                int index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (index >= 0) {
                    return cursor.getString(index);
                }
            }
        } catch (Exception ignored) {
            // A provider is allowed not to expose a display name; import then
            // fails safely because an extension cannot be validated.
        } finally {
            if (cursor != null) {
                cursor.close();
            }
        }
        return null;
    }

    private void sendAndFinish(final String payload) {
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                RomImportBridge.sendMessage(receiverObject, callbackMethod, payload);
                finish();
            }
        });
    }

    static String errorPayload(String message) {
        return "{\"status\":\"error\",\"error\":\"" + escapeJson(message) + "\"}";
    }

    private static String successPayload(String originalFileName, String relativePath, String hash, long sizeBytes) {
        return "{\"status\":\"success\","
                + "\"displayName\":\"" + escapeJson(originalFileName) + "\","
                + "\"originalFileName\":\"" + escapeJson(originalFileName) + "\","
                + "\"relativePath\":\"" + escapeJson(relativePath) + "\","
                + "\"contentHashSha256\":\"" + hash + "\","
                + "\"sizeBytes\":" + sizeBytes + "}";
    }

    private static String escapeJson(String value) {
        if (value == null) {
            return "";
        }
        StringBuilder escaped = new StringBuilder();
        for (int index = 0; index < value.length(); index++) {
            char character = value.charAt(index);
            switch (character) {
                case '\\': escaped.append("\\\\"); break;
                case '"': escaped.append("\\\""); break;
                case '\n': escaped.append("\\n"); break;
                case '\r': escaped.append("\\r"); break;
                case '\t': escaped.append("\\t"); break;
                default:
                    if (character < 0x20) {
                        escaped.append(' ');
                    } else {
                        escaped.append(character);
                    }
                    break;
            }
        }
        return escaped.toString();
    }

    private static String toHex(byte[] bytes) {
        char[] characters = new char[bytes.length * 2];
        final char[] digits = "0123456789abcdef".toCharArray();
        for (int index = 0; index < bytes.length; index++) {
            int value = bytes[index] & 0xff;
            characters[index * 2] = digits[value >>> 4];
            characters[index * 2 + 1] = digits[value & 0x0f];
        }
        return new String(characters);
    }

}
