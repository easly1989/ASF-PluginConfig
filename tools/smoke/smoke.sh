#!/usr/bin/env bash
# Smoke test against an official OS-specific ASF release, which is trimmed: installs the plugin and the demo plugin,
# starts ASF with IPC on localhost and an IPCPassword, and checks that the page is served, that the plugin's controller
# answers under /Api (401 without the password, 200 with it, plugin-defined response types serialized), that the demo
# plugin's embedded schema is discovered, and that ASF logged no error.
#
# Usage: tools/smoke/smoke.sh <unzipped-asf-release-dir> <published-plugin-dir> <published-demo-plugin-dir>
# Env: IPC_PORT (default 12422)
set -euo pipefail

if [ "$#" -ne 3 ]; then
	echo "Usage: $0 <unzipped-asf-release-dir> <published-plugin-dir> <published-demo-plugin-dir>" >&2
	exit 2
fi

asf_src=$1
plugin_src=$2
demo_src=$3
ipc_port=${IPC_PORT:-12422}
ipc="http://127.0.0.1:${ipc_port}"
password=smoke-FAKE-ipc-password
work=$(mktemp -d)
asf_pid=

# A Windows ASF build (local runs under Git Bash) needs native paths
native() {
	if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi
}

fail() {
	echo "SMOKE FAIL: $*" >&2
	echo "----- ASF output -----" >&2
	cat "$work/asf.out" >&2 2>/dev/null || true
	exit 1
}

cleanup() {
	[ -n "$asf_pid" ] && kill "$asf_pid" 2>/dev/null || true
	rm -rf "$work"
}
trap cleanup EXIT

cp -R "$asf_src"/. "$work"/
exe="$work/ArchiSteamFarm"
[ -f "$exe" ] || exe="$work/ArchiSteamFarm.exe"
[ -f "$exe" ] || fail "no ArchiSteamFarm executable in $asf_src"
chmod +x "$exe"

mkdir -p "$work/config" "$work/plugins/ASFPluginConfig" "$work/plugins/ASFPluginConfigDemo"
cp -R "$plugin_src"/. "$work/plugins/ASFPluginConfig/"
cp -R "$demo_src"/. "$work/plugins/ASFPluginConfigDemo/"

cat > "$work/config/ASF.json" <<EOF
{
	"AutoRestart": false,
	"Headless": true,
	"IPC": true,
	"IPCPassword": "${password}",
	"UpdateChannel": 0,
	"UpdatePeriod": 0
}
EOF

cat > "$work/config/IPC.config" <<EOF
{
	"Kestrel": {
		"Endpoints": {
			"HTTP": {
				"Url": "${ipc}"
			}
		}
	}
}
EOF

"$exe" --no-restart --path "$(native "$work")" > "$work/asf.out" 2>&1 &
asf_pid=$!

echo "Waiting for ASF's IPC on ${ipc}"

for _ in $(seq 1 120); do
	kill -0 "$asf_pid" 2>/dev/null || fail "ASF exited during start-up"
	curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/ASF" > /dev/null 2>&1 && break
	sleep 1
done

curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/ASF" > /dev/null || fail "IPC did not come up within 120 s"

grep -Eq 'ASFPluginConfig [0-9.]+ loaded' "$work/asf.out" || fail "the plugin did not log that it loaded"
echo "OK: $(grep -Eo 'ASFPluginConfig [0-9.]+ loaded' "$work/asf.out" | head -n 1)"

page=$(curl -fsS --max-time 20 "${ipc}/plugin-config/") || fail "the page is not served"
case "$page" in
	*"<title>Plugin config</title>"*) echo "OK: page served at /plugin-config/" ;;
	*) fail "/plugin-config/ did not return the plugin's page" ;;
esac

# The page is blank without its script and style, so a zip missing them must fail here
curl -fsS --max-time 20 "${ipc}/plugin-config/app.js" | grep -q 'asf-plugin-config.ipc-password' || fail "app.js is not served"
curl -fsS --max-time 20 "${ipc}/plugin-config/app.css" | grep -q -- '--accent' || fail "app.css is not served"
echo "OK: app.js and app.css served"

status=$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' "${ipc}/Api/PluginConfig") || fail "the controller does not answer"
[ "$status" = 401 ] || fail "without the password the controller answered $status, expected 401"
echo "OK: 401 without the password"

reply=$(curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/PluginConfig") || fail "the controller failed with the password"
echo "PluginConfig: $reply"
case "$reply" in
	*'"Success":true'*) ;;
	*) fail "the controller did not report success" ;;
esac
case "$reply" in
	*'"Name":"ASFPluginConfig"'*) echo "OK: controller answered with the plugin's response type" ;;
	*) fail "the reply lacks the plugin's response type" ;;
esac

plugins=$(curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/PluginConfig/Plugins") || fail "the plugin list failed"
case "$plugins" in
	*'"ConfigKey":"DemoNotifier"'*'"writeOnly":true'*) echo "OK: the demo plugin's schema is discovered and served" ;;
	*) fail "the plugin list lacks the demo plugin's schema: $plugins" ;;
esac
grep -Eq '1 config schema\(s\) found' "$work/asf.out" || fail "the plugin did not log one discovered schema"

curl -fsS --max-time 20 -X POST -H "Authentication: ${password}" "${ipc}/Api/ASF/Exit" > /dev/null || fail "IPC exit failed"

for _ in $(seq 1 30); do
	kill -0 "$asf_pid" 2>/dev/null || break
	sleep 1
done

if kill -0 "$asf_pid" 2>/dev/null; then
	fail "ASF did not exit within 30 s"
fi

code=0
wait "$asf_pid" || code=$?
asf_pid=
[ "$code" -eq 0 ] || fail "ASF exited with code $code"

# ASF's log lines read date|process|LEVEL|logger|message
if grep -E '\|(ERROR|FATAL)\||Exception' "$work/asf.out"; then
	fail "ASF logged an error or exception"
fi

echo "SMOKE OK"
