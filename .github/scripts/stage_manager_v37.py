from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import json
import shutil
import subprocess
import tempfile
import zipfile
from pathlib import Path


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def tree_hash(root: Path) -> str:
    h = hashlib.sha256()
    for p in sorted(x for x in root.rglob("*") if x.is_file()):
        rel = p.relative_to(root).as_posix().encode("utf-8")
        h.update(rel)
        h.update(b"\0")
        h.update(hashlib.sha256(p.read_bytes()).digest())
    return h.hexdigest()


def deterministic_zip(root: Path, output: Path) -> None:
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for p in sorted(x for x in root.rglob("*") if x.is_file()):
            info = zipfile.ZipInfo(p.relative_to(root).as_posix(), date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o100644 & 0xFFFF) << 16
            info.create_system = 3
            zf.writestr(info, p.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--repo-root", required=True, type=Path)
    args = parser.parse_args()

    source_root = args.source_root.resolve()
    repo_root = args.repo_root.resolve()
    base_zip = repo_root / "manager/packages/FreeCam_Manager_V3.6_Source.zip"
    incoming = repo_root / "manager/incoming/v3.7.0/v36-to-v37.patch.gz.b85"
    candidate = repo_root / "manager/publish-v37.candidate.json"

    with tempfile.TemporaryDirectory() as td_value:
        td = Path(td_value)
        work = td / "work"
        work.mkdir()
        with zipfile.ZipFile(base_zip, "r") as zf:
            zf.extractall(work)

        subprocess.run(["git", "init", "-q"], cwd=work, check=True)
        subprocess.run(["git", "config", "user.name", "v37-builder"], cwd=work, check=True)
        subprocess.run(["git", "config", "user.email", "v37-builder@example.invalid"], cwd=work, check=True)
        subprocess.run(["git", "add", "-A"], cwd=work, check=True)
        subprocess.run(["git", "commit", "-q", "-m", "base"], cwd=work, check=True)

        for p in list(work.iterdir()):
            if p.name == ".git":
                continue
            if p.is_dir():
                shutil.rmtree(p)
            else:
                p.unlink()
        for p in source_root.iterdir():
            dst = work / p.name
            if p.is_dir():
                shutil.copytree(p, dst)
            else:
                shutil.copy2(p, dst)

        subprocess.run(["git", "add", "-A"], cwd=work, check=True)
        patch = subprocess.check_output(
            ["git", "diff", "--cached", "--binary", "--src-prefix=", "--dst-prefix="],
            cwd=work,
        )
        if not patch:
            raise SystemExit("generated patch is empty")
        patch_sha = sha256(patch)

        incoming.parent.mkdir(parents=True, exist_ok=True)
        incoming.write_text(
            base64.b85encode(gzip.compress(patch, compresslevel=9, mtime=0)).decode("ascii") + "\n",
            encoding="ascii",
        )

        apply_check = td / "apply-check"
        apply_check.mkdir()
        with zipfile.ZipFile(base_zip, "r") as zf:
            zf.extractall(apply_check)
        patch_file = td / "patch.diff"
        patch_file.write_bytes(patch)
        subprocess.run(["git", "apply", "-p0", "--unsafe-paths", str(patch_file)], cwd=apply_check, check=True)

        expected_tree = tree_hash(source_root)
        applied_tree = tree_hash(apply_check)
        if applied_tree != expected_tree:
            raise SystemExit(f"patch apply tree mismatch expected={expected_tree} actual={applied_tree}")

        zip_path = td / "FreeCam_Manager_V3.7_Source.zip"
        deterministic_zip(source_root, zip_path)
        zip_sha = sha256(zip_path.read_bytes())
        zip_size = zip_path.stat().st_size

    cfg = {
        "schemaVersion": 1,
        "version": "3.7.0",
        "displayVersion": "V3.7",
        "packageName": "FreeCam_Manager_V3.7_Source.zip",
        "sourceSha256": zip_sha,
        "basePackageName": "FreeCam_Manager_V3.6_Source.zip",
        "patchFile": "manager/incoming/v3.7.0/v36-to-v37.patch.gz.b85",
        "patchEncoding": "base85",
        "patchCompression": "gzip",
        "patchSha256": patch_sha,
        "sourceTreeSha256": expected_tree,
        "notes": "优化 Result 选择逻辑：匹配的 Result ZIP 优先于先出现的单独日志，已测试但仍指向日志的记录会继续扫描并自动升级到 ZIP；拖拽和单击时都会按当前文件重新解析。系统状态显示“已测试”时，单击可直接在资源管理器定位当前 Result / 日志，同时保留拖拽发送。",
        "publishRevision": 1,
    }
    candidate.write_text(json.dumps(cfg, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(cfg, ensure_ascii=False, indent=2))
    print(f"deterministic_zip_size={zip_size}")


if __name__ == "__main__":
    main()
