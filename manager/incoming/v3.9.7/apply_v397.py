from pathlib import Path
import json
import sys

root = Path(sys.argv[1]) if len(sys.argv)>1 else Path(".")

def load(rel):
    p=root/rel
    return p,p.read_text(encoding="utf-8-sig")
def save(p,value):
    p.write_text(value,encoding="utf-8")
def replace_once(text,before,after,label):
    n=text.count(before)
    if n!=1: raise SystemExit(f"{label}: expected 1 occurrence, got {n}")
    return text.replace(before,after,1)

p,service=load("src-wpf/FreeCamManager.Core/Services/TestResultService.cs")
service=replace_once(service,
    "using FreeCamManager.Core.Models;\n",
    "using System.IO.Compression;\nusing System.Text.Json;\nusing System.Text.RegularExpressions;\nusing FreeCamManager.Core.Models;\n",
    "services imports")
service=replace_once(service,
    "var currentResult = FindCurrentWorkspaceResultZip(testingPath, expectedName);",
    "var currentResult = FindCurrentWorkspaceResultZip(testingPath, expectedName, build);",
    "pass current build into workspace matching")
service=replace_once(service,
    '''        var currentRaw = FindCurrentWorkspaceRawEvidence(testingPath, build);
        if (!string.IsNullOrWhiteSpace(currentRaw)) return currentRaw;

        if (IsPreferredResultZip(stored) && !IsExcludedEvidencePath(stored!, testingPath)) return stored;
''',
    '''        // A previously validated Result ZIP in 40_Result must beat any
        // current workspace log even when the exported basename differs.
        if (IsPreferredResultZip(stored) && !IsExcludedEvidencePath(stored!, testingPath)) return stored;

        var currentRaw = FindCurrentWorkspaceRawEvidence(testingPath, build);
        if (!string.IsNullOrWhiteSpace(currentRaw)) return currentRaw;
''',
    "stored ZIP before workspace raw fallback")
service=replace_once(service,
    "private static string? FindCurrentWorkspaceResultZip(string testingPath, string expectedName)",
    "private static string? FindCurrentWorkspaceResultZip(string testingPath, string expectedName, Artifact build)",
    "workspace result signature")
service=replace_once(service,
    '''            if (!string.IsNullOrWhiteSpace(numbered)) return numbered;
        }

        var recursiveExact = EnumerateEligibleFiles(testingPath, expectedName)''',
    '''            if (!string.IsNullOrWhiteSpace(numbered)) return numbered;

            // The test workspace directory may include Phase3 while its produced
            // Result ZIP omits that organizational token. Keep ownership scoped
            // to this workspace and require matching module + Test/Probe number.
            var compatible = Directory.EnumerateFiles(root, "*.zip", SearchOption.TopDirectoryOnly)
                .Where(IsPreferredResultZip)
                .Where(path => IsExpectedResultFilename(path, build))
                .Where(path => !HasConflictingForBuildId(path, build.BuildId))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(compatible)) return compatible;
        }

        var recursiveExact = EnumerateEligibleFiles(testingPath, expectedName)''',
    "current workspace compatible ZIP")
service=replace_once(service,
    '''        return EnumerateEligibleFiles(testingPath, stem + "*.zip")
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(stem + " (", StringComparison.OrdinalIgnoreCase))
            .Where(IsPreferredResultZip)
            .OrderBy(path => WorkspaceEvidenceRank(path, testingPath))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();''',
    '''        var recursiveNumbered = EnumerateEligibleFiles(testingPath, stem + "*.zip")
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(stem + " (", StringComparison.OrdinalIgnoreCase))
            .Where(IsPreferredResultZip)
            .OrderBy(path => WorkspaceEvidenceRank(path, testingPath))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(recursiveNumbered)) return recursiveNumbered;

        return EnumerateEligibleFiles(testingPath, "*.zip")
            .Where(IsPreferredResultZip)
            .Where(path => IsExpectedResultFilename(path, build))
            .Where(path => !HasConflictingForBuildId(path, build.BuildId))
            .OrderBy(path => WorkspaceEvidenceRank(path, testingPath))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();''',
    "recursive compatible ZIP")
service=replace_once(service,
    '''        return actualStem.StartsWith(expectedStem + " (", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFresh''',
    '''        if (actualStem.StartsWith(expectedStem + " (", StringComparison.OrdinalIgnoreCase)) return true;

        // WW36_Phase3_GICamera_Test3.1 -> WW36_GICamera_Test3.1_Result
        // Only the organizational PhaseN token is optional; project, feature,
        // test/probe number and all remaining tokens still have to match.
        if (!IsPreferredResultZip(path)) return false;
        return string.Equals(NormalizePhaseTokens(actualStem),
            NormalizePhaseTokens(expectedStem), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePhaseTokens(string stem)
    {
        var unnumbered = Regex.Replace(stem, @" \(\d+\)$", "", RegexOptions.CultureInvariant);
        return string.Join("_", unnumbered.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !(part.Length > 5
                && part.StartsWith("Phase", StringComparison.OrdinalIgnoreCase)
                && part[5..].All(char.IsDigit))));
    }

    private static bool HasConflictingForBuildId(string path, string buildId)
    {
        if (string.IsNullOrWhiteSpace(buildId)) return false;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                var name = Path.GetFileName(entry.FullName);
                if (!name.Equals("RESULT_MANIFEST.json", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("BUILD_MANIFEST.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Length > 1024 * 1024) return false;
                using var stream = entry.Open();
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
                foreach (var field in document.RootElement.EnumerateObject())
                {
                    if (!field.Name.Equals("ForBuildId", StringComparison.OrdinalIgnoreCase)) continue;
                    if (field.Value.ValueKind != JsonValueKind.String) return false;
                    var forBuildId = field.Value.GetString();
                    return !string.IsNullOrWhiteSpace(forBuildId)
                        && !string.Equals(forBuildId, buildId, StringComparison.OrdinalIgnoreCase);
                }
                break;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            // Legacy result packages may have no readable manifest. Their
            // matching filename and current workspace remain the fallback.
        }
        return false;
    }

    private static bool IsFresh''',
    "phase-tolerant names and explicit BuildId guard")
# Correct the inserted Python raw string regex to standard C# verbatim text.
# The normalizer for actualStem and expectedStem keeps '_Result' so both sides align.
save(p,service)
p,tests=load("src-wpf/FreeCamManager.Tests/Program.cs")
tests=replace_once(tests,
    '        await Run("V3.7 matched Result ZIP beats raw log", V37MatchedResultZipBeatsRawLog);',
    '        await Run("V3.7 matched Result ZIP beats raw log", V37MatchedResultZipBeatsRawLog);\n'
    '        await Run("V3.9.7 Phase3 WW index Result ZIP beats log", V397Phase3IndexResultZipBeatsLog);\n'
    '        await Run("V3.9.7 stored Result ZIP beats workspace raw log", V397StoredResultZipBeatsLog);',
    "register V3.9.7 regression tests")
new_tests=r'''    private static async Task V397Phase3IndexResultZipBeatsLog()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "01_Testing", "WW36_Phase3_GICamera_Test3.1");
        var results = Path.Combine(testing, "Results");
        var prior = Path.Combine(results, "Prior_Runtime_Evidence");
        Directory.CreateDirectory(prior);
        var log = Path.Combine(results, "LAUNCHER_CONSOLE.log");
        await File.WriteAllTextAsync(log, "runtime log");
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddSeconds(3));
        var priorZip = Path.Combine(prior, "WW36_GICamera_Test3.1_Result.zip");
        using (ZipFile.Open(priorZip, ZipArchiveMode.Create)) { }
        File.SetLastWriteTimeUtc(priorZip, DateTime.UtcNow.AddSeconds(5));

        var buildPath = Path.Combine(dir, "60_索引库", "WW36_Phase3_GICamera_Test3.1.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath)!);
        await File.WriteAllBytesAsync(buildPath, []);
        var build = new Artifact
        {
            Name = Path.GetFileName(buildPath),
            Path = buildPath,
            BuildId = "WW36-GICAMERA-3.1",
            TestingPath = testing,
            TestStatus = "已测试",
            ResultPath = log
        };
        var currentZip = Path.Combine(results, "WW36_GICamera_Test3.1_Result.zip");
        using (var zip = ZipFile.Open(currentZip, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("RESULT_MANIFEST.json");
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, new
            {
                ArtifactType = "Result",
                ForBuildId = build.BuildId
            });
        }

        var service = new TestResultService(new ManifestService());
        var preferred = service.ResolvePreferredDragPath(build, testing, Path.Combine(dir, "40_Result"));
        Assert(string.Equals(preferred, currentZip, StringComparison.OrdinalIgnoreCase),
            $"current WW36 Result ZIP must beat a newer raw log despite an omitted Phase3 token; got: {preferred}");
        var evidence = await service.FindEvidenceAsync(testing, build);
        Assert(evidence is not null && evidence.Kind == "ResultZip"
            && string.Equals(evidence.Path, currentZip, StringComparison.OrdinalIgnoreCase),
            "status refresh must match the current Result ZIP before logs");

        var library = LibraryService.CreateInMemory([build]);
        var refresh = new TestStatusRefreshService(library, service, dir);
        Assert(await refresh.RefreshAsync(), "existing .log association must upgrade to current Result ZIP");
        Assert(string.Equals(library.ByPath(buildPath)!.ResultPath, currentZip, StringComparison.OrdinalIgnoreCase),
            "persistent ResultPath must upgrade to the current ZIP");

        // An explicit foreign ForBuildId wins over name normalization.
        File.Delete(currentZip);
        using (var zip = ZipFile.Open(currentZip, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("RESULT_MANIFEST.json");
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, new
            {
                ArtifactType = "Result",
                ForBuildId = "SOME-OTHER-BUILD"
            });
        }
        var unsafeDrag = service.ResolvePreferredDragPath(build, testing, Path.Combine(dir, "40_Result"));
        Assert(string.Equals(unsafeDrag, log, StringComparison.OrdinalIgnoreCase),
            "conflicting ForBuildId must not be dragged as a matching result");
        var unsafeEvidence = await service.FindEvidenceAsync(testing, build);
        Assert(unsafeEvidence is not null && string.Equals(unsafeEvidence.Path, log, StringComparison.OrdinalIgnoreCase),
            "conflicting ForBuildId must not override the raw log");
    }

    private static Task V397StoredResultZipBeatsLog()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "01_Testing", "WW36_Phase3_GICamera_Test3.1");
        Directory.CreateDirectory(Path.Combine(testing, "Logs"));
        var log = Path.Combine(testing, "Logs", "LAUNCHER_CONSOLE.log");
        File.WriteAllText(log, "later log");

        var archive = Path.Combine(dir, "40_Result", "GICamera");
        Directory.CreateDirectory(archive);
        var storedZip = Path.Combine(archive, "WW36_GICamera_Test3.1_Result.zip");
        using (ZipFile.Open(storedZip, ZipArchiveMode.Create)) { }
        var build = new Artifact
        {
            Name = "WW36_Phase3_GICamera_Test3.1.zip",
            Path = Path.Combine(dir, "60_索引库", "WW36_Phase3_GICamera_Test3.1.zip"),
            TestingPath = testing,
            ResultPath = storedZip,
            TestStatus = "已测试"
        };
        var chosen = new TestResultService(new ManifestService()).ResolvePreferredDragPath(build, testing, Path.Combine(dir, "40_Result"));
        Assert(string.Equals(chosen, storedZip, StringComparison.OrdinalIgnoreCase),
            "a previously linked Result ZIP under 40_Result must beat a workspace log");
        return Task.CompletedTask;
    }

'''
tests=replace_once(tests,"    private static string TempDir()",new_tests+"    private static string TempDir()","insert V3.9.7 test implementations")
save(p,tests)
p,csproj=load("src-wpf/FreeCamManager/FreeCamManager.csproj")
csproj=replace_once(csproj,"<Version>3.9.6</Version>","<Version>3.9.7</Version>","version bump")
save(p,csproj)
p,manifest=load("BUILD_MANIFEST.json")
manifest_obj=json.loads(manifest)
manifest_obj.update({
    "Version":"V3.9.7",
    "BuildName":"FreeCam_Manager_V3.9.7",
    "Base":"FreeCam_Manager_V3.9.6",
    "Branch":"feature/manager-v3.9.7-resultzip-priority",
    "Feature":"Result ZIP priority for Phase3 WW index testing",
    "Stage":"Release",
    "BuildId":"MANAGER-V397-WW-RESULT-20260927"
})
p.write_text(json.dumps(manifest_obj,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print("Applied V3.9.7 WW index Result ZIP priority patch")
