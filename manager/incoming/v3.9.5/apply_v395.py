from pathlib import Path
import json
import subprocess
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('.')
script_dir = Path(__file__).resolve().parent


def read(rel: str):
    p = root / rel
    return p, p.read_text(encoding='utf-8-sig')


def write(p: Path, text: str):
    p.write_text(text, encoding='utf-8')


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 occurrence, found {count}')
    return text.replace(old, new, 1)


# Keep the regression test in the shipped source package.
subprocess.run([sys.executable, str(script_dir / 'apply_v395_test_only.py'), str(root)], check=True)

# 1) Unknown is a non-route. 90_Unknown remains reserved for explicit Stable_Candidate storage.
p, text = read('src-wpf/FreeCamManager.Core/Services/ClassificationService.cs')
text = replace_once(
    text,
    '        return new("Unknown", "90_Unknown");',
    '        return new("Unknown", "");',
    'unknown classification route',
)
write(p, text)

# 2) Organizer enforces the invariant even when called directly: unknown files are
# returned at their original path, not hashed as duplicates, moved, or indexed.
p, text = read('src-wpf/FreeCamManager.Core/Services/OrganizerService.cs')
insert_anchor = '''        var info = new FileInfo(path);
        a.Size = info.Length;
'''
insert_value = '''        var decision = classification.Plan(a);
        if (decision.Category == "Unknown")
        {
            a.Category = "Unknown";
            a.Status = "未识别，保留在收件箱";
            a.RelativePath = PathRebaseService.TryMakeRelative(Root, path);
            return a;
        }

        var info = new FileInfo(path);
        a.Size = info.Length;
'''
text = replace_once(text, insert_anchor, insert_value, 'organizer unknown guard')
text = replace_once(
    text,
    '        var decision = classification.Plan(a);\n        var destination = await MoveUniqueAsync(path, Path.Combine(Root, decision.RelativeDirectory), ct);',
    '        var destination = await MoveUniqueAsync(path, Path.Combine(Root, decision.RelativeDirectory), ct);',
    'organizer duplicate decision removal',
)
write(p, text)

# 3) Inbox watcher skips unknown ZIPs before extraction, and also handles unknown
# non-ZIP files returned by OrganizerService. Unchanged unknowns are remembered so
# background polling does not repeatedly inspect them; manual refresh or file changes
# cause a re-check.
p, text = read('src-wpf/FreeCamManager.Core/Services/InboxWatcherService.cs')
text = replace_once(
    text,
    '    private sealed record FileStamp(long Size, long WriteTicks, int Count);',
    '    private sealed record FileStamp(long Size, long WriteTicks, int Count, bool Unrecognized = false);',
    'file stamp unknown state',
)
zip_anchor = '''                        var decision = _classification.Plan(inspect);
                        if (decision.Category is not "Manager" and not "IndexLibrary" && _extraction.ShouldExtract(inspect))
'''
zip_value = '''                        var decision = _classification.Plan(inspect);
                        if (decision.Category == "Unknown")
                        {
                            MarkUnrecognized(path);
                            _log?.Event("SCAN_UNRECOGNIZED_KEEP_INBOX", ("path", path), ("mode", manual ? "manual" : "auto"));
                            StatusChanged?.Invoke(this, "未识别，保留在收件箱: " + name);
                            continue;
                        }
                        if (decision.Category is not "Manager" and not "IndexLibrary" && _extraction.ShouldExtract(inspect))
'''
text = replace_once(text, zip_anchor, zip_value, 'watcher zip unknown guard')
organizer_anchor = '''                    var processedArtifact = await _organizer.ProcessAsync(path, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(testingPath))
'''
organizer_value = '''                    var processedArtifact = await _organizer.ProcessAsync(path, ct).ConfigureAwait(false);
                    if (processedArtifact.Category == "Unknown")
                    {
                        MarkUnrecognized(path);
                        _log?.Event("SCAN_UNRECOGNIZED_KEEP_INBOX", ("path", path), ("mode", manual ? "manual" : "auto"));
                        StatusChanged?.Invoke(this, "未识别，保留在收件箱: " + name);
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(testingPath))
'''
text = replace_once(text, organizer_anchor, organizer_value, 'watcher organizer unknown guard')
ready_anchor = '''                if (_seen.TryGetValue(path, out var previous) && previous.Size == info.Length && previous.WriteTicks == info.LastWriteTimeUtc.Ticks)
                    stamp = previous with { Count = previous.Count + 1 };
                else
                    stamp = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks, 1);
'''
ready_value = '''                if (_seen.TryGetValue(path, out var previous) && previous.Size == info.Length && previous.WriteTicks == info.LastWriteTimeUtc.Ticks)
                {
                    if (previous.Unrecognized) return false;
                    stamp = previous with { Count = previous.Count + 1 };
                }
                else
                    stamp = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks, 1);
'''
text = replace_once(text, ready_anchor, ready_value, 'watcher unchanged unknown skip')
helper_anchor = '''    private static bool IsTemporaryDownload(string name)
'''
helper_value = '''    private void MarkUnrecognized(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            lock (_gate) _seen[path] = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks, 2, true);
        }
        catch
        {
            // A disappearing inbox file needs no remembered state.
        }
    }

    private static bool IsTemporaryDownload(string name)
'''
text = replace_once(text, helper_anchor, helper_value, 'watcher unknown stamp helper')
write(p, text)

# 4) Version and build metadata.
p, text = read('src-wpf/FreeCamManager/FreeCamManager.csproj')
text = replace_once(text, '<Version>3.9.4</Version>', '<Version>3.9.5</Version>', 'project version')
write(p, text)

manifest_path = root / 'BUILD_MANIFEST.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig')) if manifest_path.exists() else {}
manifest.update({
    'Version': 'V3.9.5',
    'BuildName': 'FreeCam_Manager_V3.9.5',
    'Base': 'FreeCam_Manager_V3.9.4',
    'Branch': 'feature/manager-v3.9.5-unrecognized-inbox',
    'Feature': 'Unrecognized Files Stay In Inbox',
    'Stage': 'Release',
    'BuildId': 'MANAGER-V395-INBOX-20260917',
})
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

print('Applied FreeCam Manager V3.9.5 unrecognized-inbox patch')
