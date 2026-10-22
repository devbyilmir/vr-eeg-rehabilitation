
from pathlib import Path
import warnings

import joblib
import mne
import numpy as np
import pandas as pd

from mne.decoding import CSP
from sklearn.discriminant_analysis import LinearDiscriminantAnalysis
from sklearn.model_selection import LeaveOneGroupOut, cross_val_score
from sklearn.pipeline import Pipeline
from sklearn.metrics import balanced_accuracy_score, accuracy_score


# =========================
# CONFIGURATION
# =========================

DATASET_DIR = Path(r"C:\nm000158_v1.0.5")
MODEL_PATH = Path("motor_imagery_model.joblib")

CHANNELS = ["C3", "Cz", "C4"]
CLASS_NAMES = ["left_hand", "right_hand"]

WINDOW_START = 0.5
WINDOW_END = 3.5

SKIP_SUBJECTS = {"sub-5", "sub-15", "sub-49"}

mne.set_log_level("ERROR")
warnings.filterwarnings("ignore", category=RuntimeWarning)


def find_channel(raw, target):
    """Find an EEG channel by its name."""
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


def load_dataset():
    X = []
    y = []
    groups = []

    files = sorted(
        DATASET_DIR.glob("sub-*/ses-*/eeg/*_eeg.bdf")
    )

    if not files:
        raise FileNotFoundError(
            f"No BDF files found in {DATASET_DIR}\n"
            "Check DATASET_DIR and the dataset folder structure."
        )

    print(f"Found BDF files: {len(files)}")

    for bdf_path in files:
        subject = bdf_path.parts[-4]

        if subject in SKIP_SUBJECTS:
            print(f"Skipping {subject}")
            continue

        events_path = bdf_path.with_name(
            bdf_path.name.replace("_eeg.bdf", "_events.tsv")
        )

        if not events_path.exists():
            print(f"Events file not found: {events_path}")
            continue

        try:
            events = pd.read_csv(events_path, sep="\t")

            required = {"onset", "trial_type"}
            if not required.issubset(events.columns):
                print(f"Unexpected event columns: {events_path}")
                continue

            raw = mne.io.read_raw_bdf(
                str(bdf_path),
                preload=True,
                verbose=False,
            )

            channel_map = {
                channel: find_channel(raw, channel)
                for channel in CHANNELS
            }

            if any(value is None for value in channel_map.values()):
                print(
                    f"Required channels not found for {subject}: "
                    f"{channel_map}"
                )
                continue

            raw.pick_channels(list(channel_map.values()))

            # Rename channels consistently across recordings.
            reverse_map = {
                actual: standard
                for standard, actual in channel_map.items()
            }
            raw.rename_channels(reverse_map)

            # Motor imagery frequency band.
            raw.filter(
                l_freq=8.0,
                h_freq=30.0,
                verbose=False,
            )

            sfreq = raw.info["sfreq"]
            subject_trials = 0

            for _, event in events.iterrows():
                label = str(event["trial_type"]).strip().lower()

                if label not in CLASS_NAMES:
                    continue

                onset = float(event["onset"])
                start_time = onset + WINDOW_START
                end_time = onset + WINDOW_END

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
                    round((WINDOW_END - WINDOW_START) * sfreq)
                )

                if epoch.shape[1] != expected_samples:
                    continue

                X.append(epoch)
                y.append(label)
                groups.append(subject)
                subject_trials += 1

            print(f"{subject}: loaded {subject_trials} trials")

        except Exception as exc:
            print(f"Error processing {bdf_path}: {exc}")

    if not X:
        raise RuntimeError(
            "No trials loaded. Check event labels, channel names "
            "and dataset structure."
        )

    X = np.asarray(X, dtype=np.float64)
    y = np.asarray(y)
    groups = np.asarray(groups)

    print("\nDataset loaded")
    print(f"Trials: {len(X)}")
    print(f"EEG shape: {X.shape}")
    print(f"Subjects: {len(np.unique(groups))}")
    print(f"Classes: {dict(zip(*np.unique(y, return_counts=True)))}")

    return X, y, groups


def create_model():
    return Pipeline([
        (
            "csp",
            CSP(
                n_components=2,
                reg=None,
                log=True,
                norm_trace=False,
            ),
        ),
        (
            "lda",
            LinearDiscriminantAnalysis(),
        ),
    ])


def main():
    X, y, groups = load_dataset()

    if len(np.unique(y)) != 2:
        raise RuntimeError(
            "Expected both left_hand and right_hand classes."
        )

    unique_subjects = np.unique(groups)

    if len(unique_subjects) < 2:
        raise RuntimeError(
            "At least two subjects are required for "
            "leave-one-subject-out evaluation."
        )

    print("\nEvaluating model by held-out subject...")

    model = create_model()
    cv = LeaveOneGroupOut()

    scores = cross_val_score(
        model,
        X,
        y,
        groups=groups,
        cv=cv,
        scoring="accuracy",
        n_jobs=1,
    )

    print(f"Mean accuracy: {scores.mean():.3f}")
    print(f"Std accuracy:  {scores.std():.3f}")
    print(f"Per-fold scores: {np.round(scores, 3)}")

    # Train the final pipeline on all available trials.
    print("\nTraining final model on all trials...")
    model.fit(X, y)

    artifact = {
        "model": model,
        "channels": CHANNELS,
        "classes": model.named_steps["lda"].classes_.tolist(),
        "window_start": WINDOW_START,
        "window_end": WINDOW_END,
        "filter_band": [8.0, 30.0],
        "sampling_frequency": 500,
        "training_subjects": unique_subjects.tolist(),
        "evaluation": {
            "method": "LeaveOneGroupOut",
            "mean_accuracy": float(scores.mean()),
            "std_accuracy": float(scores.std()),
        },
    }

    joblib.dump(artifact, MODEL_PATH)

    print(f"\nModel saved to: {MODEL_PATH.resolve()}")
    print("Done.")


if __name__ == "__main__":
    main()
