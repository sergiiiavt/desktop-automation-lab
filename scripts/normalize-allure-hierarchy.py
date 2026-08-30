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
            group = "Python - Notepad"
        elif "nunit" in framework or full_name.startswith("Notepad.Tests"):
            group = "FlaUI - Notepad"
        else:
            print(f"Hierarchy unchanged for {path.name}: framework={framework!r}, fullName={full_name!r}")
            continue

        # Allure 3 Awesome report's Tests tree is driven by titlePath.
        # Adapter defaults such as ["python", "tests", "test_notepad.py"] and
        # ["Notepad", "Tests", "NotepadTests", ...] therefore must be replaced
        # directly; changing only suite/package labels does not affect that tree.
        data["titlePath"] = [group]
        data["labels"] = replace_single_label(labels, "package", group)
        data["labels"] = replace_single_label(data["labels"], "parentSuite", group)
        data["labels"] = replace_single_label(data["labels"], "suite", group)

        path.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        print(f"Allure Tests tree: {path.name} -> {group}; titlePath={data['titlePath']}")
        changed += 1

    print(f"Normalized Allure hierarchy for {changed}/{len(files)} result files.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
