package com.firstxr.mediapipe;

import android.content.Context;
import android.graphics.Bitmap;

import com.google.mediapipe.framework.image.BitmapImageBuilder;
import com.google.mediapipe.framework.image.MPImage;
import com.google.mediapipe.tasks.components.containers.Category;
import com.google.mediapipe.tasks.components.containers.NormalizedLandmark;
import com.google.mediapipe.tasks.core.BaseOptions;
import com.google.mediapipe.tasks.vision.core.RunningMode;
import com.google.mediapipe.tasks.vision.handlandmarker.HandLandmarker;
import com.google.mediapipe.tasks.vision.handlandmarker.HandLandmarkerResult;

import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Small JNI-friendly adapter around MediaPipe Tasks Vision. Unity supplies a downscaled RGBA
 * frame; all Bitmap conversion and model inference run on one Java worker. At most one frame is
 * in flight so camera load cannot build an unbounded inference queue.
 */
public final class HandLandmarkerBridge implements AutoCloseable {
    private static final int LANDMARK_COUNT = 21;
    private static final int VALUES_PER_LANDMARK = 3;
    private static final int RESULT_HEADER_LENGTH = 4;

    private final Object resultLock = new Object();
    private final Object landmarkerLock = new Object();
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final AtomicBoolean busy = new AtomicBoolean(false);
    private volatile HandLandmarker handLandmarker;
    private volatile boolean closed;
    private volatile String status = "initializing";
    private double[] latestResult;

    public HandLandmarkerBridge(Context context, String modelAssetPath) {
        if (context == null) {
            status = "Android context is null";
            return;
        }

        try {
            BaseOptions baseOptions = BaseOptions.builder()
                    .setModelAssetPath(modelAssetPath)
                    .build();
            HandLandmarker.HandLandmarkerOptions options = HandLandmarker.HandLandmarkerOptions.builder()
                    .setBaseOptions(baseOptions)
                    .setRunningMode(RunningMode.VIDEO)
                    .setNumHands(1)
                    .setMinHandDetectionConfidence(0.50f)
                    .setMinHandPresenceConfidence(0.50f)
                    .setMinTrackingConfidence(0.50f)
                    .build();
            synchronized (landmarkerLock) {
                handLandmarker = HandLandmarker.createFromOptions(context.getApplicationContext(), options);
            }
            status = "ready";
        } catch (Throwable error) {
            status = "initialization failed: " + summarize(error);
        }
    }

    public boolean isReady() {
        return !closed && handLandmarker != null;
    }

    public boolean isBusy() {
        return busy.get();
    }

    public String getStatus() {
        return status;
    }

    /** Returns false when inference is already busy. The caller should drop that frame. */
    public boolean submitRgba(
            final byte[] rgba,
            final int width,
            final int height,
            final long timestampMs,
            final long sourceSequence,
            final double observedAtSeconds) {
        if (!isReady() || rgba == null || width <= 0 || height <= 0
                || rgba.length < width * height * 4 || !busy.compareAndSet(false, true)) {
            return false;
        }

        executor.execute(() -> infer(rgba, width, height, timestampMs, sourceSequence, observedAtSeconds));
        return true;
    }

    /**
     * Atomically consumes the latest result. Layout: sequence, observed seconds, confidence,
     * tracked flag, then 21 x/y/z normalized landmarks. An empty array means no new result.
     */
    public double[] consumeLatestResult() {
        synchronized (resultLock) {
            if (latestResult == null)
                return new double[0];
            double[] result = latestResult;
            latestResult = null;
            return result;
        }
    }

    private void infer(
            byte[] rgba,
            int width,
            int height,
            long timestampMs,
            long sourceSequence,
            double observedAtSeconds) {
        Bitmap bitmap = null;
        MPImage image = null;
        try {
            if (!isReady())
                return;

            int[] argb = new int[width * height];
            for (int pixel = 0, offset = 0; pixel < argb.length; pixel++, offset += 4) {
                int red = rgba[offset] & 0xff;
                int green = rgba[offset + 1] & 0xff;
                int blue = rgba[offset + 2] & 0xff;
                int alpha = rgba[offset + 3] & 0xff;
                argb[pixel] = (alpha << 24) | (red << 16) | (green << 8) | blue;
            }

            bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
            bitmap.setPixels(argb, 0, width, 0, 0, width, height);
            image = new BitmapImageBuilder(bitmap).build();
            HandLandmarkerResult result;
            synchronized (landmarkerLock) {
                if (closed || handLandmarker == null)
                    return;
                result = handLandmarker.detectForVideo(image, timestampMs);
            }
            publish(result, sourceSequence, observedAtSeconds);
            status = "running";
        } catch (Throwable error) {
            status = "inference failed: " + summarize(error);
            publish(null, sourceSequence, observedAtSeconds);
        } finally {
            if (image != null) {
                try { image.close(); } catch (Throwable ignored) { }
            }
            if (bitmap != null)
                bitmap.recycle();
            busy.set(false);
        }
    }

    private void publish(HandLandmarkerResult result, long sequence, double observedAtSeconds) {
        double[] packet = new double[RESULT_HEADER_LENGTH + LANDMARK_COUNT * VALUES_PER_LANDMARK];
        packet[0] = sequence;
        packet[1] = observedAtSeconds;

        List<List<NormalizedLandmark>> hands = result == null ? null : result.landmarks();
        if (hands == null || hands.isEmpty() || hands.get(0).size() < LANDMARK_COUNT) {
            packet[2] = 0.0;
            packet[3] = 0.0;
        } else {
            float confidence = 1.0f;
            List<List<Category>> handedness = result.handednesses();
            if (handedness != null && !handedness.isEmpty() && !handedness.get(0).isEmpty())
                confidence = handedness.get(0).get(0).score();
            packet[2] = confidence;
            packet[3] = 1.0;

            List<NormalizedLandmark> landmarks = hands.get(0);
            for (int index = 0; index < LANDMARK_COUNT; index++) {
                NormalizedLandmark point = landmarks.get(index);
                int output = RESULT_HEADER_LENGTH + index * VALUES_PER_LANDMARK;
                packet[output] = point.x();
                packet[output + 1] = point.y();
                packet[output + 2] = point.z();
            }
        }

        synchronized (resultLock) {
            latestResult = packet;
        }
    }

    @Override
    public void close() {
        closed = true;
        executor.shutdownNow();
        synchronized (landmarkerLock) {
            HandLandmarker instance = handLandmarker;
            handLandmarker = null;
            if (instance != null) {
                try { instance.close(); } catch (Throwable ignored) { }
            }
        }
        status = "closed";
        synchronized (resultLock) {
            latestResult = null;
        }
    }

    private static String summarize(Throwable error) {
        String message = error.getMessage();
        return error.getClass().getSimpleName() + (message == null ? "" : ": " + message);
    }
}
