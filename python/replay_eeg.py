
import asyncio
from pathlib import Path

import joblib
import mne
import numpy as np
import pandas as pd
import websockets


# =========================
# CONFIGURATION
# =========================

MODEL_PATH = Path("motor_imagery_model.joblib")

DATASET_DIR = Path(r"C:\nm000158_v1.0.5")

BDF_PATH = (
    DATASET_DIR
    / "sub-1"
    / "ses-0"
    / "eeg"
    / "sub-1_ses-0_task-imagery_run-0_eeg.bdf"
)

EVENTS_PATH = BDF_PATH.with_name(
    BDF_PATH.name.replace("_eeg.bdf", "_events.tsv")
)

SERVER_URL = "ws://127.0.0.1:8000/ws/python"

REPLAY_SPEED = 1.0
PAUSE_BETWEEN_PREDICTIONS = 1.0

mne.set_log_level("ERROR")


def find_channel(raw, target):
    target = target.upper()

    for name in raw.ch_names:
        normalized = (
            name.upper()
            .replace(" ", "")
            .replace("-", "")
            .replace("_", "")
        )

        if normalized == target or normalized.endswith(target):
            return name

    return None


def load_recording(artifact):
    if not BDF_PATH.exists():
        raise FileNotFoundError(
            f"EEG file not found:\n{BDF_PATH}"
        )

    if not EVENTS_PATH.exists():
        raise FileNotFoundError(
            f"Events file not found:\n{EVENTS_PATH}"
        )

    print(f"Loading EEG recording:\n{BDF_PATH}")

    raw = mne.io.read_raw_bdf(
        str(BDF_PATH),
        preload=True,
        verbose=False,
    )

    channel_names = artifact["channels"]
    channel_map = {
        channel: find_channel(raw, channel)
        for channel in channel_names
    }

    if any(value is None for value in channel_map.values()):
        raise RuntimeError(
            f"Required EEG channels are missing: {channel_map}"
        )

    raw.pick_channels(list(channel_map.values()))

    raw.rename_channels({
        actual: standard
        for standard, actual in channel_map.items()
    })

    raw.filter(
        l_freq=artifact["filter_band"][0],
        h_freq=artifact["filter_band"][1],
        verbose=False,
    )

    events = pd.read_csv(EVENTS_PATH, sep="\t")

    required = {"onset", "trial_type"}
    if not required.issubset(events.columns):
        raise RuntimeError(
            f"Unexpected event columns: {events.columns.tolist()}"
        )

    return raw, events


async def main():
    if not MODEL_PATH.exists():
        raise FileNotFoundError(
            f"Model not found: {MODEL_PATH.resolve()}\n"
            "Run python train_eeg.py first."
        )

    artifact = joblib.load(MODEL_PATH)
    model = artifact["model"]

    raw, events = load_recording(artifact)

    sfreq = raw.info["sfreq"]
    window_start = float(artifact["window_start"])
    window_end = float(artifact["window_end"])

    print("\nModel loaded.")
    print(f"Classes: {artifact['classes']}")
    print(f"Recording sampling rate: {sfreq} Hz")
    print(f"Replay speed: {REPLAY_SPEED}x")
    print(f"Connecting to {SERVER_URL} ...")

    async with websockets.connect(SERVER_URL) as websocket:
        print("Connected to FastAPI.")
        print("Starting EEG playback...\n")

        sent = 0

        for _, event in events.iterrows():
            label = str(event["trial_type"]).strip().lower()

            if label not in ("left_hand", "right_hand"):
                continue

            onset = float(event["onset"])
            start_time = onset + window_start
            end_time = onset + window_end

            start_sample = int(round(start_time * sfreq))
            end_sample = int(round(end_time * sfreq))

            if (
                start_sample < 0
                or end_sample > raw.n_times
                or end_sample <= start_sample
            ):
                continue

            epoch = raw.get_data(
                start=start_sample,
                stop=end_sample,
            )

            expected_samples = int(
                round((window_end - window_start) * sfreq)
            )

            if epoch.shape[1] != expected_samples:
                continue

            # CSP expects shape: trials x channels x samples.
            prediction = model.predict(epoch[np.newaxis, :, :])[0]

            # Send the model's prediction, not the event's true label.
            message = {
                "predicted_class": str(prediction),
                "source": "dataset_replay",
            }

            await websocket.send(
                __import__("json").dumps(message)
            )

            sent += 1

            print(
                f"Trial {sent:02d} | "
                f"onset={onset:.2f}s | "
                f"prediction={prediction} | "
                f"dataset label={label}"
            )

            await asyncio.sleep(
                PAUSE_BETWEEN_PREDICTIONS / REPLAY_SPEED
            )

        print(f"\nPlayback finished. Predictions sent: {sent}")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nPlayback stopped.")
