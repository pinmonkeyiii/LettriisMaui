
# Lettriis (.NET MAUI)

This folder is a MAUI port scaffold of your Python/Pygame Lettriis project.

## What's already ported
- Core falling-piece loop (move/rotate/soft drop/hard drop, lock, gravity tick)
- Word scan & removal (horizontal + vertical, longest-first, per-column collapse)
- Combo multiplier concept (decay/growth)
- Definition quiz overlay (simplified): +50 on correct, else push letters up + add row
- Banned-word normalization/filter based on your `word_filter.py`

## What you need to copy into Resources/Raw
Copy these from the Python repo into `LettriisMaui/Resources/Raw/`:

- `scrabble_dictionary.txt` (your real word list)
- `banned_words.txt` (if you use it)

Optional:
- Sound files: `sounds/rotate.wav`, `sounds/clear_word.wav`, `sounds/level_up.wav`
- Theme json + assets: `themes/default/theme.json` and referenced files under `Resources/Raw/themes/default/`

## Run
Open `LettriisMaui.sln` (or `LettriisMaui/LettriisMaui.csproj`) in Visual Studio 2026, restore packages, and run.
