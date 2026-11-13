"""Isolated real keyserver + GUI publishing regression, on Linux or Windows.

Use a disposable, built game mirror. All catalog entries and keys are fixture
identities; the driver enters them through the actual catalog controls.
"""
import argparse
import datetime
import hashlib
import http.server
import json
import os
import re
import shutil
import urllib.error
from pathlib import Path
import subprocess
import tempfile
import threading
import time
import urllib.request
import zipfile


class PublicCatalog(http.server.BaseHTTPRequestHandler):
    leaked = False

    def do_GET(self):
        type(self).leaked |= bool(self.headers.get("Authorization"))
        raw = json.dumps({"catalog": {"version": 1}, "mods": [
            {"id": "public_test_pack", "name": "Public catalog test", "version": "1.0.0", "category": "other"}
        ], "collections": []}).encode()
        self.send_response(200)
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def log_message(self, *_args):
        pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True, help="disposable game mirror, never the working tree")
    parser.add_argument("--server", type=Path, required=True)
    parser.add_argument("--issuer", type=Path, required=True)
    parser.add_argument("--godot", required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--suite", action="append", choices=["publish_gui", "publishing", "catalog_servers", "private_catalog", "mod_units"])
    parser.add_argument("--windowed", action="store_true")
    parser.add_argument("--local-launcher", type=Path, help="run the actual run_local.ps1 in an isolated keyserver copy before GUI publication")
    parser.add_argument("--powershell", default="powershell.exe" if os.name == "nt" else "pwsh")
    args = parser.parse_args()
    project, output = args.project.resolve(), args.out.resolve()
    if project == Path(__file__).resolve().parents[4]:
        parser.error("--project must be a disposable mirror")
    output.mkdir(parents=True, exist_ok=True)
    config = json.loads((project / "config/mod_config.json").read_text(encoding="utf-8"))
    if config.get("catalog_urls") or config.get("catalog_token_files"):
        parser.error("mirror must have empty catalog_urls and catalog_token_files (prevent boot network access)")
    suites = args.suite or ["publish_gui", "publishing", "catalog_servers", "private_catalog", "mod_units"]
    results = []
    for suite in suites:
        with tempfile.TemporaryDirectory(prefix="publish-dialog-") as temporary:
            fixture = Path(temporary)
            def cli(binary, *arguments):
                result = subprocess.run([str(binary.resolve()), *arguments], cwd=fixture, capture_output=True, timeout=30)
                if result.returncode:
                    raise RuntimeError(binary.name + " " + arguments[0] + ": " + result.stderr.decode(errors="replace"))
            cli(args.issuer, "init", "--out", "keys", "--audience", "publish-dialog-e2e")
            for filename, name, author, sections in [
                ("local.token", "Reader", "", "local_test"),
                ("publish.token", "Publisher", "integration-author", "local_test"),
                ("renewed.token", "Publisher Renamed", "integration-author", "local_test"),
                ("masked.token", "Masked Author", "integration-author", "admin_*")]:
                arguments = ["issue", "--issuer", "keys/issuer.json", "--user", name, "--sections", sections, "--out", filename]
                if author:
                    arguments += ["--author-id", author]
                cli(args.issuer, *arguments)
                cli(args.server, "import", "--record", filename + ".record.json")
            cli(args.issuer, "issue", "--issuer", "keys/issuer.json", "--user", "Administrator", "--admin", "--expires",
                (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=1)).strftime("%Y-%m-%dT%H:%M:%SZ"), "--out", "admin.token")
            cli(args.server, "import", "--record", "admin.token.record.json")
            if suite in ("catalog_servers", "private_catalog"):
                archives = fixture / "private/archives"
                archives.mkdir(parents=True)
                archive = archives / "probe.zip"
                with zipfile.ZipFile(archive, "w") as zip:
                    zip.writestr("manifest.json", json.dumps({"id": "private_test_pack", "version": "1.0.0", "name": "Private catalog test", "category": "other"}))
                    zip.writestr("data/check.txt", "Private catalog download works.")
                source = {"sections": [{"id": "local_test", "mods": [{"id": "private_test_pack", "version": "1.0.0", "name": "Private catalog test", "category": "other", "downloads": [{"version": "1.0.0", "file_ids": ["probe"]}]}]}],
                          "files": [{"id": "probe", "path": "probe.zip", "name": "probe.zip", "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()}]}
                (fixture / "source.json").write_text(json.dumps(source))
                cli(args.server, "import-catalog", "--file", "source.json")
            if suite == "publishing":
                disk = fixture / "private/mods/admin_storage/test_storage_pack/1.0.0"
                disk.mkdir(parents=True)
                (disk / "manifest.json").write_text(json.dumps({"id": "test_storage_pack", "version": "1.0.0", "name": "Filesystem storage test", "category": "other"}))
                (disk / "content.txt").write_text("Storage discovered automatically.")
                (disk / ".ready").touch()
            launcher_key = None
            if args.local_launcher:
                if suite != "publish_gui":
                    parser.error("--local-launcher requires --suite publish_gui")
                # Execute the same PowerShell path used by run_local.bat. A prior
                # read-only local.token must neither block issuance nor be overwritten.
                scripts = fixture / "scripts"
                binaries = fixture / "bin"
                scripts.mkdir()
                binaries.mkdir()
                shutil.copy2(args.local_launcher, scripts / "run_local.ps1")
                shutil.copy2(args.server, binaries / "keyserver.exe")
                shutil.copy2(args.issuer, binaries / "keyissuer.exe")
                original_reader = (fixture / "local.token").read_bytes()
                command = [args.powershell, "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                           str(scripts / "run_local.ps1"), "-PrepareOnly", "-DataRoot", str(fixture), "-Port", "18080"]
                prepared = subprocess.run(command, cwd=fixture, capture_output=True, timeout=60)
                if prepared.returncode:
                    raise RuntimeError("run_local.ps1 failed: " + prepared.stdout.decode(errors="replace") + prepared.stderr.decode(errors="replace"))
                (output / "local-launcher-prepare.log").write_bytes(prepared.stdout + prepared.stderr)
                configured = json.loads((fixture / "game-config.example.json").read_text(encoding="utf-8-sig"))
                launcher_key = Path(configured["catalog_token_files"]["http://127.0.0.1:18080/v1/catalog"])
                original_author = launcher_key.read_bytes()
                repeated = subprocess.run(command, cwd=fixture, capture_output=True, timeout=60)
                assert repeated.returncode == 0, "launcher restart failed"
                assert launcher_key.read_bytes() == original_author, "launcher restart changed the publisher identity"
                assert (fixture / "local.token").read_bytes() == original_reader, "launcher overwrote a previously issued reader key"
                # Also exercise first launch with no master keys or tokens.
                fresh = fixture / "fresh"
                fresh_command = command.copy()
                fresh_command[fresh_command.index("-DataRoot") + 1] = str(fresh)
                clean = subprocess.run(fresh_command, cwd=fixture, capture_output=True, timeout=60)
                assert clean.returncode == 0, "first launch failed"
                fresh_config = json.loads((fresh / "game-config.example.json").read_text(encoding="utf-8-sig"))
                fresh_key = Path(fresh_config["catalog_token_files"]["http://127.0.0.1:18080/v1/catalog"])
                registry = json.loads((fresh / "keys/registry.json").read_text(encoding="utf-8-sig"))
                fresh_grant = registry["keys"][0]
                assert fresh_grant["author_id"] and fresh_grant["sections"] == ["local_test"] and not fresh_grant["admin"], "first launch did not issue a bounded author key"
                assert fresh_key.name == "local-author.token" and fresh_key.is_file(), "first launch advertises the wrong key"
                (output / "local-launcher-fresh.json").write_text(json.dumps({"author_id_present": True, "sections": fresh_grant["sections"], "is_admin": False, "key_filename": fresh_key.name}, indent=2))
            public = http.server.ThreadingHTTPServer(("127.0.0.1", 0), PublicCatalog)
            thread = threading.Thread(target=public.serve_forever, daemon=True)
            thread.start()
            env = os.environ.copy()
            profile = fixture / "profile"
            env.update({"XDG_DATA_HOME": str(profile), "APPDATA": str(profile), "LOCALAPPDATA": str(profile),
                        "XDG_CONFIG_HOME": str(profile / "config"), "XDG_CACHE_HOME": str(profile / "cache"),
                        "CTHANGOVER_TEST_CACHE": str(output / "compilation-cache"),
                        "CTHANGOVER_TEST_PROJECT": str(project), "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT": "1",
                        "CTHANGOVER_PRIVATE_CATALOG_URL": "http://127.0.0.1:18080/v1/catalog",
                        "CTHANGOVER_PUBLIC_CATALOG_URL": f"http://127.0.0.1:{public.server_port}/catalog.json"})
            for variable, filename in [("PRIVATE", "local"), ("PUBLISH", "publish"), ("PUBLISH_RENEWED", "renewed"), ("PUBLISH_ADMIN", "admin"), ("PUBLISH_MASKED", "masked")]:
                env["CTHANGOVER_" + variable + "_TOKEN_FILE"] = str(fixture / (filename + ".token"))
            if launcher_key:
                env["CTHANGOVER_PUBLISH_TOKEN_FILE"] = str(launcher_key)
                env["CTHANGOVER_PUBLISH_EXPECTED_AUTHOR"] = "local-tester"
            try:
                with (output / (suite + "-server.log")).open("wb") as server_log:
                    server = subprocess.Popen([str(args.server.resolve()), "serve", "--listen", "127.0.0.1:18080", "--public-base-url", "http://127.0.0.1:18080", "--insecure-local-http"], cwd=fixture, stdout=server_log, stderr=server_log)
                    try:
                        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
                        for attempt in range(100):
                            try:
                                with opener.open("http://127.0.0.1:18080/v1/catalog", timeout=1) as response:
                                    assert json.load(response)["mods"] == []
                                break
                            except OSError:
                                if server.poll() is not None or attempt == 99:
                                    raise
                                time.sleep(.05)
                        if launcher_key:
                            request = urllib.request.Request("http://127.0.0.1:18080/v1/publishing",
                                headers={"Authorization": "Bearer " + launcher_key.read_text().strip()})
                            try:
                                with opener.open(request, timeout=5) as response:
                                    permissions = json.load(response)
                                    status = response.status
                            except urllib.error.HTTPError as response:
                                permissions, status = {}, response.code
                            contract = {"status": status, "author_id_present": bool(permissions.get("author_id")),
                                        "sections": permissions.get("sections"), "is_admin": permissions.get("is_admin"),
                                        "reader_key_preserved": True, "restart_preserves_key": True}
                            (output / "local-launcher-contract.json").write_text(json.dumps(contract, indent=2))
                            assert status == 200 and contract["author_id_present"] and "local_test" in contract["sections"], "run_local.bat -> run_local.ps1 produced a key without publishing rights"
                            assert not contract["is_admin"], "local author must not require administrator rights"
                        display = ["--windowed", "--resolution", "1600x1000"] if args.windowed else ["--headless"]
                        arguments = ["--run-tests", "--mod=mod_manager"] if suite == "mod_units" else ["--itest=" + suite]
                        command = [args.godot, "--path", str(project), *display, "--", "--frontend", *arguments]
                        result = subprocess.run(command, cwd=project, env=env, capture_output=True, timeout=160)
                        raw = result.stdout + b"\nSTDERR:\n" + result.stderr
                        if any(path.read_bytes().strip() in raw for path in fixture.rglob("*.token")):
                            raise RuntimeError("bearer in game output; output withheld")
                        text = raw.decode("utf-8", errors="replace")
                        (output / (suite + ".log")).write_text(text, encoding="utf-8")
                        marker = "[TEST_RESULT] name=" + suite + " passed=1 failed=0"
                        valid = result.returncode == 0 and (marker in text if suite != "mod_units" else bool(re.search(r"\[TEST_RESULT\] name=mod_units:mod_manager passed=[1-9][0-9]* failed=0", text)))
                        errors = [error for error in ("error CS", "ERROR:", "ObjectDB instances leaked", "resources still in use", "RID allocations", "/v1/download/") if error in text]
                        # The frontend fixture has no gameplay scenes; the same
                        # exact diagnostic is expected by private_real_mods.
                        expected_empty_scenes = 0
                        for line in text.splitlines():
                            plain = re.sub(r"\x1b\[[0-9;]*m", "", line)
                            message = plain.split("] ", 2)[-1]
                            if message == "CollectSceneDefinitions — no scene definitions found":
                                expected_empty_scenes += 1
                            elif "\x1b[31m" in line or "[ERROR]" in plain:
                                errors.append(message)
                        if expected_empty_scenes != 1:
                            errors.append("frontend scene diagnostic count differs")
                        valid &= not errors and not PublicCatalog.leaked
                        results.append({"suite": suite, "passed": valid, "exit": result.returncode, "errors": errors})
                        print(json.dumps(results[-1]), flush=True)
                    finally:
                        server.terminate()
                        server.wait(timeout=10)
            finally:
                public.shutdown()
                public.server_close()
                thread.join(timeout=2)
    (output / "summary.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    if not all(item["passed"] for item in results):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
