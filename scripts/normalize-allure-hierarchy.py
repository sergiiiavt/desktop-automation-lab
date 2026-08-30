import json
import sys
from pathlib import Path


def framework_name(labels: list[dict]) -> str:
    for label in labels:
        if label.get("name") == "framework":
            return str(label.get("value", "")).lower()
    return ""


def replace_single_label(labels: list[dict], name: str, value: str) -> list[dict]:
    result = [label for label in labels if label.get("name") != name]
    result.append({"name": name, "value": value})
    return result


def main() -> int:
    results_dir = Path(sys.argv[1] if len(sys.argv) > 1 else "allure-results")
    files = sorted(results_dir.glob("*-result.json"))

    if not files:
        print(f"No Allure result JSON files found in {results_dir}")
        return 1

    changed = 0
    for path in files:
        data = json.loads(path.read_text(encoding="utf-8"))
        labels = list(data.get("labels") or [])
        framework = framework_name(labels)
        full_name = str(data.get("fullName", ""))

        if "pytest" in framework or full_name.startswith("python.") or "test_notepad" in full_name:
            package = "Python - Notepad"
        elif "nunit" in framework or full_name.startswith("Notepad.Tests"):
            package = "FlaUI - Notepad"
        else:
            print(f"Hierarchy unchanged for {path.name}: framework={framework!r}, fullName={full_name!r}")
            continue

        data["labels"] = replace_single_label(labels, "package", package)
        path.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        print(f"Allure package: {path.name} -> {package}")
        changed += 1

    print(f"Normalized Allure hierarchy for {changed}/{len(files)} result files.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
