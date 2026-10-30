import os
import subprocess
from pathlib import Path

SKIP_DIRS = {'.git', '.github', '.mvn', "obj", "Debug"}
SCRIPT_PATH = Path(__file__).resolve()

def get_file_content_with_name(file_path):
    """Read file content and return with filename header"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        return f"=== {file_path} ===\n{content}\n\n"
    except Exception as e:
        return f"=== {file_path} ===\n[ERROR: Could not read file - {str(e)}]\n\n"

def scan_and_collect_files():
    """Scan current directory (including src) for files and collect their content"""
    cwd = Path.cwd()
    collected_content = []
    file_count = 0

    def process_file(file_path):
        nonlocal file_count
        try:
            if file_path.resolve() == SCRIPT_PATH:
                return
        except OSError:
            return
        relative_path = file_path.relative_to(cwd)
        content = get_file_content_with_name(relative_path)
        collected_content.append(content)
        file_count += 1
        print(f"Added: {relative_path}")

    # Walk the entire current directory tree
    for root, dirs, files in os.walk(cwd):
        # Prune skip directories in-place so os.walk doesn't descend into them
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]

        for file in files:
            file_path = Path(root) / file

            # Skip the script itself
            try:
                if file_path.resolve() == SCRIPT_PATH:
                    continue
            except OSError:
                continue

            # Skip any file inside a skipped directory (safety net)
            if any(part in SKIP_DIRS for part in file_path.relative_to(cwd).parts[:-1]):
                continue

            process_file(file_path)

    return ''.join(collected_content), file_count

def copy_to_clipboard(text):
    """Copy text to clipboard using platform-specific commands"""
    try:
        import pyperclip
        pyperclip.copy(text)
        return True
    except ImportError:
        try:
            if os.name == 'nt':  # Windows
                subprocess.run(['clip'], input=text.encode('utf-16'), check=True)
            elif os.name == 'posix':
                try:
                    subprocess.run(['pbcopy'], input=text.encode('utf-8'), check=True)
                except FileNotFoundError:
                    subprocess.run(['xclip', '-selection', 'clipboard'],
                                 input=text.encode('utf-8'), check=True)
            return True
        except Exception as e:
            print(f"Error copying to clipboard: {e}")
            return False

def main():
    print("Scanning current directory for files...")
    print("-" * 50)

    result = scan_and_collect_files()

    if result is None:
        return

    content, file_count = result

    if file_count == 0:
        print("\nNo files found.")
        return

    print(f"\n{'=' * 50}")
    print(f"Found {file_count} files.")
    print(f"Total characters: {len(content)}")
    print(f"Total lines: {content.count(chr(10))}")
    print(f"{'=' * 50}")

    if copy_to_clipboard(content):
        print("✓ Successfully copied to clipboard!")
        print("\nPreview of first 500 characters:")
        print("-" * 50)
        print(content[:500])
        if len(content) > 500:
            print("\n... (truncated)")
    else:
        print("✗ Failed to copy to clipboard.")
        print("\nContent preview (first 1000 characters):")
        print("-" * 50)
        print(content[:1000])
        if len(content) > 1000:
            print("\n... (truncated)")

if __name__ == "__main__":
    main()