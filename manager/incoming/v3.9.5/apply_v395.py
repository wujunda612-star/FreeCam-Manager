from pathlib import Path
import json
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('.')

def read(rel):
    p = root / rel
    return p, p.read_text(encoding='utf-8-sig')

def write(p, text):
    p.write_text(text, encoding='utf-8')

def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 occurrence, found {count}')
    return text.replace(old, new, 1)

# Organizer safety gate: unknown artifacts stay exactly where they are and are not indexed/routed as duplicates.
p, text = read('src-wpf/FreeCamManager.Core/Services/OrganizerService.cs')
text = replace_once(
    text,
    '''        var info = new FileInfo(path);
        a.Size = info.Length;
        a.Sha256 = await hash.FileSha256Async(path, ct);
        a.ImportedAt = DateTimeOffset.Now.ToString("O");
''',
    '''        var decision = classification.Plan(a);
        if (decision.Category == "Unknown")
        {
            a.Path = path;
            a.RelativePath = "";
            a.Category = "Unknown";
            a.Status = "未识别，保留在收件箱";
            return a;
        }

        var info = new FileInfo(path);
        a.Size = info.Length;
        a.Sha256 = await hash.FileSha256Async(path, ct);
        a.ImportedAt = DateTimeOffset.Now.ToString("O");
''',
    'Organizer unknown guard'
)
text = replace_once(
    text,
    '''        var decision = classification.Plan(a);
        var destination = await MoveUniqueAsync(path, Path.Combine(Root, decision.RelativeDirectory), ct);
''',
    '''        var destination = await MoveUniqueAsync(path, Path.Combine(Root, decision.RelativeDirectory), ct);
''',
    'Organizer decision reuse'
)
write(p, text)

# Watcher gate: classify before extraction/organizing, leave unknown files in Inbox,
# and cache unchanged unknown files to avoid re-inspecting them every polling cycle.
p, text = read('src-wpf/FreeCamManager.Core/Services/InboxWatcherService.cs')
text = replace_once(
    text,
    '''    private readonly Dictionary<string, FileStamp> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
''',
    '''    private readonly Dictionary<string, FileStamp> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileStamp> _unrecognized = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
''',
    'Watcher unknown cache field'
)
text = replace_once(
    text,
    '''                present.Add(path);

                if (!await IsReadyForProcessingAsync(path, manual, ct).ConfigureAwait(false)) continue;
''',
    '''                present.Add(path);

                if (!manual && IsRememberedUnrecognized(path)) continue;
                if (!await IsReadyForProcessingAsync(path, manual, ct).ConfigureAwait(false)) continue;
''',
    'Watcher unknown cache gate'
)
old_block = '''                    string testingPath = "";
                    if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        Artifact inspect;
                        try { inspect = await _manifest.InspectAsync(path, ct).ConfigureAwait(false); }
                        catch { inspect = _manifest.InspectFilename(name); inspect.Path = path; }
                        var decision = _classification.Plan(inspect);
                        if (decision.Category is not "Manager" and not "IndexLibrary" && _extraction.ShouldExtract(inspect))
                        {
                            var result = await _extraction.ExtractToTestingAsync(path, SettingsService.TestingRoot(cfg), ct).ConfigureAwait(false);
                            testingPath = result.Destination;
                            _log?.Event("TEST_FOLDER_READY", ("path", path), ("folder", testingPath), ("status", result.Status));
                        }
                    }
'''
new_block = '''                    string testingPath = "";
                    Artifact inspect;
                    if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        try { inspect = await _manifest.InspectAsync(path, ct).ConfigureAwait(false); }
                        catch { inspect = _manifest.InspectFilename(name); inspect.Path = path; }
                    }
                    else
                    {
                        inspect = _manifest.InspectFilename(name);
                        inspect.Path = path;
                    }

                    var decision = _classification.Plan(inspect);
                    if (decision.Category == "Unknown")
                    {
                        RememberUnrecognized(path);
                        _log?.Event("SCAN_UNRECOGNIZED_KEEP_INBOX", ("path", path), ("mode", manual ? "manual" : "auto"));
                        StatusChanged?.Invoke(this, "未识别，已保留在收件箱: " + name);
                        continue;
                    }

                    if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && decision.Category is not "Manager" and not "IndexLibrary"
                        && _extraction.ShouldExtract(inspect))
                    {
                        var result = await _extraction.ExtractToTestingAsync(path, SettingsService.TestingRoot(cfg), ct).ConfigureAwait(false);
                        testingPath = result.Destination;
                        _log?.Event("TEST_FOLDER_READY", ("path", path), ("folder", testingPath), ("status", result.Status));
                    }
'''
text = replace_once(text, old_block, new_block, 'Watcher pre-route classification')
text = replace_once(
    text,
    '''                    lock (_gate) _seen.Remove(path);
                    processed++;
''',
    '''                    lock (_gate)
                    {
                        _seen.Remove(path);
                        _unrecognized.Remove(path);
                    }
                    processed++;
''',
    'Watcher successful cleanup'
)
text = replace_once(
    text,
    '''            lock (_gate)
            {
                foreach (var path in _seen.Keys.Where(x => !present.Contains(x)).ToList()) _seen.Remove(path);
            }
''',
    '''            lock (_gate)
            {
                foreach (var path in _seen.Keys.Where(x => !present.Contains(x)).ToList()) _seen.Remove(path);
                foreach (var path in _unrecognized.Keys.Where(x => !present.Contains(x)).ToList()) _unrecognized.Remove(path);
            }
''',
    'Watcher absent cleanup'
)
helper = '''    private bool IsRememberedUnrecognized(string path)
    {
        FileInfo info;
        try { info = new FileInfo(path); }
        catch { return false; }
        if (!info.Exists) return false;

        lock (_gate)
        {
            if (_unrecognized.TryGetValue(path, out var stamp)
                && stamp.Size == info.Length
                && stamp.WriteTicks == info.LastWriteTimeUtc.Ticks)
                return true;

            _unrecognized.Remove(path);
            return false;
        }
    }

    private void RememberUnrecognized(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            lock (_gate)
                _unrecognized[path] = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks, 1);
        }
        catch { }
    }

'''
text = replace_once(
    text,
    '''    private async Task<bool> IsReadyForProcessingAsync(string path, bool manual, CancellationToken ct)
''',
    helper + '''    private async Task<bool> IsReadyForProcessingAsync(string path, bool manual, CancellationToken ct)
''',
    'Watcher unknown cache helpers'
)
write(p, text)

# Version metadata.
p, text = read('src-wpf/FreeCamManager/FreeCamManager.csproj')
text = replace_once(text, '<Version>3.9.4</Version>', '<Version>3.9.5</Version>', 'project version')
write(p, text)

manifest_path = root / 'BUILD_MANIFEST.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
manifest.update({
    'Version': 'V3.9.5',
    'BuildName': 'FreeCam_Manager_V3.9.5',
    'Base': 'FreeCam_Manager_V3.9.4',
    'Branch': 'feature/manager-v3.9.5-inbox-unknown',
    'Feature': 'Keep Unrecognized Files In Inbox',
    'Stage': 'Release',
    'BuildId': 'MANAGER-V395-INBOX-UNKNOWN-20260919',
})
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

print('Applied FreeCam Manager V3.9.5 inbox unknown routing patch')
