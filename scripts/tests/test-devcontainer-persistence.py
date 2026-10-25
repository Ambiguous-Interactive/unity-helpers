#!/usr/bin/env python3
"""Regression tests for persistent agent state and conservative container recovery."""

import hashlib
import io
import os
import shutil
import sqlite3
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TARGETS = (
    ".codex",
    ".claude",
    ".claude-zai",
    ".claude-openrouter",
    ".config/opencode",
    ".local/share/opencode",
    ".local/state/opencode",
    ".config/nanocoder",
    ".local/share/nanocoder",
    ".copilot",
    ".config/gh",
    ".mcp-auth",
)


class MountTests(unittest.TestCase):
    def test_all_personal_state_has_workspace_scoped_volumes(self):
        config = (ROOT / ".devcontainer/devcontainer.json").read_text()
        for relative in TARGETS:
            with self.subTest(relative=relative):
                self.assertRegex(
                    config,
                    r"source=unity-helpers-\$\{devcontainerId\}-[^,]+,target=/home/vscode/"
                    + relative.replace(".", r"\.")
                    + r",type=volume",
                )


class RecoveryTests(unittest.TestCase):
    def run_case(self, status, probe="", foreign=False, inspect_failure=False):
        with tempfile.TemporaryDirectory(prefix="container-recovery-") as directory:
            temporary = Path(directory)
            fake = temporary / "docker"
            fake.write_text("""#!/usr/bin/env python3
import json, os, sys, time
from pathlib import Path
args = sys.argv[1:]
if args[0] == 'ps': print('candidate')
elif args[0] == 'inspect':
    if os.environ['INSPECT_FAILURE'] == '1': sys.exit(1)
    print(json.dumps([{'Config': {'Labels': {'devcontainer.local_folder': os.environ['WORKSPACE']}}, 'State': {'Status': os.environ['STATUS']}}]))
elif args[0] == 'exec':
    error = os.environ['PROBE']
    if error == 'timeout': time.sleep(3)
    if error: print(error, file=sys.stderr); sys.exit(1)
elif args[0] == 'rm': Path(os.environ['REMOVED']).write_text('removed')
else: sys.exit(2)
""")
            fake.chmod(0o755)
            removed = temporary / "removed"
            environment = dict(
                os.environ,
                PATH=str(temporary) + os.pathsep + os.environ["PATH"],
                WORKSPACE=str(ROOT) + ("-other" if foreign else ""),
                STATUS=status,
                PROBE=probe,
                REMOVED=str(removed),
                INSPECT_FAILURE=str(int(inspect_failure)),
            )
            result = subprocess.run(
                [
                    "pwsh",
                    "-NoProfile",
                    "-File",
                    str(ROOT / ".devcontainer/initialize-host.ps1"),
                    "-ExecProbeTimeoutSeconds",
                    "1",
                ],
                env=environment,
                check=False,
                capture_output=True,
                text=True,
                timeout=15,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            return removed.exists()

    def test_normal_states_are_preserved(self):
        for state in ("created", "exited", "paused", "restarting", "running"):
            with self.subTest(state=state):
                self.assertFalse(self.run_case(state))

    def test_unknown_probe_failures_are_preserved(self):
        for failure in (
            "permission denied",
            "OCI runtime exec failed: error executing setns process: operation not permitted",
            "Cannot connect to the Docker daemon",
            "timeout",
        ):
            with self.subTest(failure=failure):
                self.assertFalse(self.run_case("running", failure))

    def test_dead_and_confirmed_zombies_are_removed(self):
        self.assertTrue(self.run_case("dead"))
        self.assertTrue(
            self.run_case(
                "running",
                "OCI runtime exec failed: error executing setns process: failed to open /proc/42/ns/ipc: no such file or directory",
            )
        )

    def test_foreign_and_uninspectable_containers_are_preserved(self):
        self.assertFalse(self.run_case("dead", foreign=True))
        self.assertFalse(self.run_case("dead", inspect_failure=True))

    def test_missing_docker_does_not_block_opening(self):
        pwsh = shutil.which("pwsh")
        with tempfile.TemporaryDirectory(prefix="missing-docker-") as directory:
            result = subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(ROOT / ".devcontainer/initialize-host.ps1"),
                ],
                env=dict(os.environ, PATH=directory),
                check=False,
                capture_output=True,
                text=True,
                timeout=10,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("docker CLI not found", result.stdout)


class StateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="agent-state-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.home = self.root / "home"
        self.home.mkdir()
        self.workspace = self.root / "workspace"
        self.workspace.mkdir()
        self.archive = self.workspace / "progress/devcontainer-state/migration.tar.gz"

    def run_state(self, command, home=None, success=True):
        result = subprocess.run(
            [
                "python3",
                str(ROOT / ".devcontainer/agent-state.py"),
                command,
                "--home",
                str(home or self.home),
                "--workspace",
                str(self.workspace),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )
        if success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0)
        return result

    def populate(self):
        for relative in TARGETS:
            directory = self.home / relative
            directory.mkdir(parents=True)
            (directory / "synthetic-data").write_text("preserve " + relative)
        (self.home / ".codex/auth.json").write_text('{"token":"synthetic"}')
        (self.home / ".codex/sessions").mkdir()
        (self.home / ".codex/sessions/conversation.jsonl").write_text(
            '{"prompt":"synthetic history"}\n'
        )
        (self.home / ".claude.json").write_text('{"theme":"dark"}')

    def test_round_trip_all_tools_and_sqlite_snapshot(self):
        self.populate()
        database = self.home / ".codex/state.sqlite"
        with sqlite3.connect(database) as connection:
            connection.execute("PRAGMA journal_mode=WAL")
            connection.execute("CREATE TABLE state (value TEXT)")
            connection.execute("INSERT INTO state VALUES ('synthetic conversation')")
            connection.commit()
            self.run_state("backup")
        self.assertEqual(self.archive.stat().st_mode & 0o777, 0o600)
        destination = self.root / "restored"
        self.run_state("restore", destination)
        for relative in TARGETS:
            self.assertEqual(
                (destination / relative / "synthetic-data").read_text(),
                "preserve " + relative,
            )
        self.assertEqual(
            (destination / ".claude/.claude.json").read_text(), '{"theme":"dark"}'
        )
        with sqlite3.connect(destination / ".codex/state.sqlite") as connection:
            self.assertEqual(
                connection.execute("PRAGMA integrity_check").fetchone()[0], "ok"
            )
            self.assertEqual(
                connection.execute("SELECT value FROM state").fetchone()[0],
                "synthetic conversation",
            )

    def test_restore_conflict_does_not_modify_any_destination(self):
        self.populate()
        self.run_state("backup")
        destination = self.root / "restored"
        (destination / ".mcp-auth").mkdir(parents=True)
        (destination / ".mcp-auth/keep").write_text("keep")
        self.run_state("restore", destination, success=False)
        self.assertFalse((destination / ".codex").exists())
        self.assertEqual((destination / ".mcp-auth/keep").read_text(), "keep")

    def test_corrupted_archive_is_rejected(self):
        self.populate()
        self.run_state("backup")
        with self.archive.open("ab") as stream:
            stream.write(b"corrupt")
        destination = self.root / "restored"
        self.run_state("restore", destination, success=False)
        self.assertFalse(destination.exists())

    def test_restore_rejects_existing_parent_symlink(self):
        self.populate()
        self.run_state("backup")
        destination = self.root / "restored"
        destination.mkdir()
        external = self.root / "external"
        external.mkdir()
        (destination / ".config").symlink_to(external)
        self.run_state("restore", destination, success=False)
        self.assertFalse((destination / ".codex").exists())
        self.assertEqual(list(external.iterdir()), [])

    def test_backup_refuses_overwrite(self):
        self.populate()
        self.run_state("backup")
        original = self.archive.read_bytes()
        self.run_state("backup", success=False)
        self.assertEqual(self.archive.read_bytes(), original)

    def test_prepare_restores_once_and_preserves_later_changes(self):
        self.populate()
        self.run_state("backup")
        destination = self.root / "restored"
        self.run_state("prepare", destination)
        changed = destination / ".codex/auth.json"
        changed.write_text("new refreshed token")
        self.run_state("prepare", destination)
        self.assertEqual(changed.read_text(), "new refreshed token")
        self.assertTrue(self.archive.exists())

    def test_prepare_preserves_user_settings_and_links_generated_mcp(self):
        config = self.home / ".codex/config.toml"
        config.parent.mkdir()
        original = (
            'model = "personal-model"\ncli_auth_credentials_store = "ephemeral"\n'
        )
        config.write_text(original)
        generated = self.workspace / ".copilot/mcp-config.json"
        generated.parent.mkdir()
        generated.write_text('{"mcpServers":{}}')
        result = self.run_state("prepare")
        self.assertIn("WARNING", result.stdout)
        self.assertEqual(config.read_text(), original)
        self.assertEqual(
            (self.home / ".copilot/mcp-config.json").resolve(), generated.resolve()
        )
        self.run_state("prepare")
        for relative in TARGETS:
            self.assertEqual((self.home / relative).stat().st_mode & 0o777, 0o700)

    def test_prepare_defaults_file_auth_without_changing_existing_config(self):
        self.run_state("prepare")
        config = self.home / ".codex/config.toml"
        self.assertIn('cli_auth_credentials_store = "file"', config.read_text())
        config.write_text('model = "keep"\n')
        self.run_state("prepare")
        self.assertEqual(config.read_text(), 'model = "keep"\n')

    def test_legacy_copilot_and_external_links_survive(self):
        (self.workspace / ".copilot").mkdir()
        (self.workspace / ".copilot/config.json").write_text("synthetic auth")
        (self.home / ".codex").mkdir()
        (self.home / ".codex/skills").symlink_to("/opt/image-skills")
        self.run_state("backup")
        destination = self.root / "restored"
        self.run_state("restore", destination)
        self.assertEqual(
            (destination / ".copilot/config.json").read_text(), "synthetic auth"
        )
        self.assertEqual(
            os.readlink(destination / ".codex/skills"), "/opt/image-skills"
        )

    def test_future_backup_uses_migrated_copilot_state(self):
        (self.workspace / ".copilot").mkdir()
        (self.workspace / ".copilot/config.json").write_text("old token")
        self.run_state("backup")
        destination = self.root / "restored"
        self.run_state("prepare", destination)
        (destination / ".copilot/config.json").write_text("refreshed token")
        later = self.workspace / "later.tar.gz"
        result = subprocess.run(
            [
                "python3",
                str(ROOT / ".devcontainer/agent-state.py"),
                "backup",
                str(later),
                "--home",
                str(destination),
                "--workspace",
                str(self.workspace),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        with tarfile.open(later) as archive:
            self.assertEqual(
                archive.extractfile(".copilot/config.json").read(), b"refreshed token"
            )

    def test_path_traversal_archive_is_rejected_before_writing(self):
        self.archive.parent.mkdir(parents=True)
        with tarfile.open(self.archive, "w:gz") as bundle:
            member = tarfile.TarInfo("../escaped")
            member.size = 4
            bundle.addfile(member, io.BytesIO(b"nope"))
        self.archive.with_suffix(".gz.sha256").write_text(
            hashlib.sha256(self.archive.read_bytes()).hexdigest()
        )
        self.run_state("restore", success=False)
        self.assertFalse((self.root / "escaped").exists())


class InstallerTests(unittest.TestCase):
    def test_update_and_offline_fallback_preserve_personal_state(self):
        with tempfile.TemporaryDirectory(prefix="agent-update-") as directory:
            home = Path(directory)
            prefix = home / ".local"
            binaries = prefix / "bin"
            binaries.mkdir(parents=True)
            npm = binaries / "npm"
            npm.write_text("""#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
packages = {'@openai/codex': 'codex', '@anthropic-ai/claude-code': 'claude', 'opencode-ai': 'opencode', '@nanocollective/nanocoder': 'nanocoder', '@z_ai/mcp-server': 'zai-mcp-server', 'mcp-remote': 'mcp-remote'}
if sys.argv[1] == 'view':
    if os.environ.get('TEST_OFFLINE'): sys.exit(1)
    print('2.0.0')
elif sys.argv[1] == 'install':
    package, version = sys.argv[3].rsplit('@', 1)
    prefix = Path(os.environ['NPM_CONFIG_PREFIX'])
    target = prefix / 'lib/node_modules' / package / 'package.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps({'version': version}))
    binary = prefix / 'bin' / packages[package]
    binary.write_text('#!/bin/sh\\necho ' + version + '\\n')
    binary.chmod(0o755)
else: sys.exit(2)
""")
            npm.chmod(0o755)
            initial = prefix / "lib/node_modules/@openai/codex/package.json"
            initial.parent.mkdir(parents=True)
            initial.write_text('{"version":"1.0.0"}')
            codex = binaries / "codex"
            codex.write_text("#!/bin/sh\necho 1.0.0\n")
            codex.chmod(0o755)
            self.assertEqual(
                subprocess.check_output([str(codex), "--version"], text=True).strip(),
                "1.0.0",
            )
            state = home / ".codex/auth.json"
            state.parent.mkdir()
            state.write_text("synthetic refreshed credential")
            environment = dict(
                os.environ,
                HOME=str(home),
                NPM_CONFIG_PREFIX=str(prefix),
                PATH=str(binaries) + os.pathsep + os.environ["PATH"],
            )
            installer = str(ROOT / ".devcontainer/install-agent-clis.sh")
            result = subprocess.run(
                ["bash", installer],
                env=environment,
                check=False,
                capture_output=True,
                text=True,
                timeout=20,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            version = subprocess.check_output(
                [str(binaries / "codex"), "--version"], text=True
            ).strip()
            self.assertEqual(version, "2.0.0")
            environment["TEST_OFFLINE"] = "1"
            result = subprocess.run(
                ["bash", installer],
                env=environment,
                check=False,
                capture_output=True,
                text=True,
                timeout=20,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("Registry unreachable; keeping installed", result.stdout)
            self.assertEqual(
                subprocess.check_output(
                    [str(binaries / "codex"), "--version"], text=True
                ).strip(),
                version,
            )
            self.assertEqual(state.read_text(), "synthetic refreshed credential")


@unittest.skipUnless(
    os.environ.get("DEVCONTAINER_PERSISTENCE_IMAGE"),
    "Set DEVCONTAINER_PERSISTENCE_IMAGE to a local devcontainer image for real Docker checks",
)
class DockerPersistenceTests(unittest.TestCase):
    def test_replacement_permissions_and_database_integrity(self):
        import uuid

        identifier = "agent-state-test-" + uuid.uuid4().hex[:12]
        image = identifier + ":test"
        containers, volumes = [], []

        def docker(*arguments, **kwargs):
            result = subprocess.run(
                ["docker", *arguments],
                check=False,
                capture_output=True,
                text=True,
                timeout=60,
                **kwargs,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            return result.stdout.strip()

        try:
            source = (ROOT / ".devcontainer/Dockerfile").read_text()
            block = source[
                source.index("RUN mkdir -p /home/vscode/.codex") : source.index(
                    "RUN mkdir -p /home/vscode/.npm"
                )
            ]
            docker(
                "build",
                "-t",
                image,
                "-",
                input="FROM "
                + os.environ["DEVCONTAINER_PERSISTENCE_IMAGE"]
                + "\nUSER root\n"
                + block,
            )
            mounts = []
            for index, relative in enumerate(TARGETS):
                volume = identifier + "-" + str(index)
                volumes.append(volume)
                mounts.extend(
                    [
                        "--mount",
                        f"type=volume,source={volume},target=/home/vscode/{relative}",
                    ]
                )
            for iteration in range(2):
                container = identifier + "-container-" + str(iteration)
                containers.append(container)
                docker(
                    "run",
                    "-d",
                    "--name",
                    container,
                    *mounts,
                    "--entrypoint",
                    "/bin/sh",
                    image,
                    "-c",
                    "sleep 120",
                )
                if iteration == 0:
                    code = f"""from pathlib import Path
import sqlite3
home = Path('/home/vscode')
for relative in {TARGETS!r}:
    path = home / relative
    assert path.stat().st_uid == 1000
    (path / 'synthetic').write_text('persistent')
with sqlite3.connect(home / '.codex/state.sqlite') as connection:
    connection.execute('CREATE TABLE history (prompt TEXT)')
    connection.execute("INSERT INTO history VALUES ('synthetic conversation')")
"""
                    docker("exec", "--user", "vscode", container, "python3", "-c", code)
                    docker(
                        "exec",
                        container,
                        "chown",
                        "-R",
                        "1234:1234",
                        "/home/vscode/.codex",
                    )
                else:
                    lifecycle = (ROOT / ".devcontainer/post-start.sh").read_text()
                    repair = lifecycle[
                        lifecycle.index("USER_OWNED_DIRS=(") : lifecycle.index(
                            'log_ok "Volume mount ownership verified"'
                        )
                    ]
                    docker(
                        "exec",
                        "--user",
                        "vscode",
                        container,
                        "bash",
                        "-c",
                        'set -euo pipefail; log_ok() { :; }; log_warn() { echo "$*" >&2; }; '
                        + repair,
                    )
                    code = f"""from pathlib import Path
import sqlite3
home = Path('/home/vscode')
for relative in {TARGETS!r}:
    path = home / relative
    assert (path / 'synthetic').read_text() == 'persistent'
    (path / 'still-writable').write_text('yes')
assert (home / '.codex').stat().st_uid == 1000
with sqlite3.connect(home / '.codex/state.sqlite') as connection:
    assert connection.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
    assert connection.execute('SELECT prompt FROM history').fetchone()[0] == 'synthetic conversation'
"""
                    docker("exec", "--user", "vscode", container, "python3", "-c", code)
                docker("rm", "-f", container)
                containers.remove(container)
        finally:
            for container in containers:
                subprocess.run(
                    ["docker", "rm", "-f", container], check=False, capture_output=True
                )
            for volume in volumes:
                subprocess.run(
                    ["docker", "volume", "rm", volume], check=False, capture_output=True
                )
            subprocess.run(
                ["docker", "image", "rm", image], check=False, capture_output=True
            )


if __name__ == "__main__":
    unittest.main()
