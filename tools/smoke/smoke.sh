#!/usr/bin/env bash
# Smoke test against an official OS-specific ASF release, which is trimmed: installs the plugin and the demo plugin,
# starts ASF with IPC on localhost, and checks
# - with an IPCPassword: the page and its assets are served; the controller answers 401 without the password and 200
#   with it; the demo plugin's embedded schema is discovered; a global save is validated (400), version-checked (409),
#   written into ASF.json without touching other keys, and makes ASF exit to apply it (AutoRestart is off); a bot save
#   writes that bot's file and ASF keeps running; unknown or path-like bot names get 404; the stored secret is never
#   returned, only named as set, and a save that omits it keeps it in ASF.json;
# - without an IPCPassword: a save is refused with 403;
# - without an IPCPassword but with AllowWritesWithoutIPCPassword on: ASF logs the warning, the host reports it, and a
#   save that clears the secret is written;
# - the update button is on the page and ASF's own POST /Api/Plugins/Update answers its request;
# - ASF logged no error or exception in any run.
#
# Usage: tools/smoke/smoke.sh <unzipped-asf-release-dir> <published-plugin-dir> <published-demo-plugin-dir>
# Env: IPC_PORT (default 12422), PYTHON (default python3)
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
python=${PYTHON:-python3}
password=smoke-FAKE-ipc-password
token=smoke-FAKE-access-token
work=$(mktemp -d)
asf_pid=

# A Windows ASF build (local runs under Git Bash) needs native paths
native() {
	if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi
}

fail() {
	echo "SMOKE FAIL: $*" >&2
	echo "----- ASF output -----" >&2
	cat "$work"/asf*.out >&2 2>/dev/null || true
	exit 1
}

cleanup() {
	[ -n "$asf_pid" ] && kill "$asf_pid" 2>/dev/null || true
	rm -rf "$work"
}
trap cleanup EXIT

# The Authentication header, or a harmless one when the run has no IPC password
auth() {
	if [ -n "$1" ]; then printf 'Authentication: %s' "$1"; else printf 'X-No-Auth: 1'; fi
}

# start_asf <log-name> <ipc-password-or-empty>
start_asf() {
	"$exe" --no-restart --path "$(native "$work")" > "$work/$1" 2>&1 &
	asf_pid=$!
	echo "Waiting for ASF's IPC on ${ipc}"

	for _ in $(seq 1 120); do
		kill -0 "$asf_pid" 2>/dev/null || fail "ASF exited during start-up"
		curl -fsS --max-time 20 -H "$(auth "$2")" "${ipc}/Api/PluginConfig" > /dev/null 2>&1 && return 0
		sleep 1
	done

	fail "IPC did not come up within 120 s"
}

# wait_exit <seconds>: ASF must exit by itself, with code 0
wait_exit() {
	for _ in $(seq 1 "$1"); do
		kill -0 "$asf_pid" 2>/dev/null || break
		sleep 1
	done

	if kill -0 "$asf_pid" 2>/dev/null; then
		fail "ASF did not exit within $1 s"
	fi

	code=0
	wait "$asf_pid" || code=$?
	asf_pid=
	[ "$code" -eq 0 ] || fail "ASF exited with code $code"
}

# save_global <ipc-password-or-empty> <json-body> prints the HTTP status; the answer goes to $work/save.json
save_global() {
	curl -sS --max-time 20 -o "$work/save.json" -w '%{http_code}' -X POST -H "$(auth "$1")" -H 'Content-Type: application/json' -d "$2" "${ipc}/Api/PluginConfig/Plugins/DemoNotifier/Global"
}

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
	"UpdatePeriod": 0,
	"DemoNotifier": { "ServerUrl": "https://old.example.org", "AccessToken": "${token}", "Legacy": "kept" }
}
EOF

cat > "$work/config/smoke.json" <<EOF
{
	"Enabled": false,
	"DemoNotifier": { "Enabled": true, "Unknown": 1 }
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

start_asf asf1.out "$password"

grep -Eq 'ASFPluginConfig [0-9.]+ loaded' "$work/asf1.out" || fail "the plugin did not log that it loaded"
echo "OK: $(grep -Eo 'ASFPluginConfig [0-9.]+ loaded' "$work/asf1.out" | head -n 1)"

page=$(curl -fsS --max-time 20 "${ipc}/plugin-config/") || fail "the page is not served"
case "$page" in
	*"<title>Plugin config</title>"*) echo "OK: page served at /plugin-config/" ;;
	*) fail "/plugin-config/ did not return the plugin's page" ;;
esac

# The page is blank without its scripts and style, so a zip missing them must fail here
curl -fsS --max-time 20 "${ipc}/plugin-config/app.js" | grep -q 'asf-plugin-config.ipc-password' || fail "app.js is not served"
curl -fsS --max-time 20 "${ipc}/plugin-config/form.js" | grep -q 'SchemaForm' || fail "form.js is not served"
curl -fsS --max-time 20 "${ipc}/plugin-config/app.css" | grep -q -- '--accent' || fail "app.css is not served"
echo "OK: app.js, form.js and app.css served"
case "$page" in
	*'id="update"'*) echo "OK: the page has the update button" ;;
	*) fail "the page lacks the update button" ;;
esac

status=$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' "${ipc}/Api/PluginConfig") || fail "the controller does not answer"
[ "$status" = 401 ] || fail "without the password the controller answered $status, expected 401"
echo "OK: 401 without the password"

# The update button posts to ASF's own endpoint; with the default PluginsUpdateMode nothing is updated or downloaded
update=$(curl -fsS --max-time 60 -X POST -H "Authentication: ${password}" -H 'Content-Type: application/json' -d '{}' "${ipc}/Api/Plugins/Update") || fail "ASF's plugin update endpoint failed"
case "$update" in
	*'"Success":true'*) echo "OK: ASF's plugin update endpoint answers the page's request" ;;
	*) fail "unexpected plugin update answer: $update" ;;
esac

reply=$(curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/PluginConfig") || fail "the controller failed with the password"
case "$reply" in
	*'"Success":true'*) ;;
	*) fail "the controller did not report success: $reply" ;;
esac
case "$reply" in
	*'"Name":"ASFPluginConfig"'*) echo "OK: controller answered with the plugin's response type" ;;
	*) fail "the reply lacks the plugin's response type: $reply" ;;
esac

plugins=$(curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/PluginConfig/Plugins") || fail "the plugin list failed"
case "$plugins" in
	*'"ConfigKey":"DemoNotifier"'*'"writeOnly":true'*) echo "OK: the demo plugin's schema is discovered and served" ;;
	*) fail "the plugin list lacks the demo plugin's schema: $plugins" ;;
esac
case "$plugins" in
	*'"ConfigKey":"ASFPluginConfig"'*) echo "OK: the host's own schema is listed" ;;
	*) fail "the plugin list lacks the host's own schema: $plugins" ;;
esac
grep -Eq '2 config schema\(s\) found' "$work/asf1.out" || fail "the plugin did not log two discovered schemas (its own and the demo's)"

bots=$(curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/PluginConfig/Bots") || fail "the bot list failed"
case "$bots" in
	*'"Result":["smoke"]'*) echo "OK: bot list" ;;
	*) fail "unexpected bot list: $bots" ;;
esac

for bot in missing ASF ..%2FASF ..%5CASF; do
	code=$(curl -sS --max-time 20 -o /dev/null -w '%{http_code}' -H "Authentication: ${password}" "${ipc}/Api/PluginConfig/Plugins/DemoNotifier/Bots/${bot}")
	[ "$code" = 404 ] || fail "bot ${bot} answered $code, expected 404"
done
echo "OK: 404 for unknown and path-like bot names"

bot_version=$(curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/PluginConfig/Plugins/DemoNotifier/Bots/smoke" | sed -n 's/.*"Version":"\([0-9A-F]*\)".*/\1/p')
[ -n "$bot_version" ] || fail "no version for the bot value"
code=$(curl -sS --max-time 20 -o "$work/save.json" -w '%{http_code}' -X POST -H "Authentication: ${password}" -H 'Content-Type: application/json' -d "{\"Value\":{\"Enabled\":false,\"Events\":{\"FarmingFinished\":true}},\"Version\":\"${bot_version}\"}" "${ipc}/Api/PluginConfig/Plugins/DemoNotifier/Bots/smoke")
[ "$code" = 200 ] || fail "a valid bot save answered $code: $(cat "$work/save.json")"
"$python" - "$work/config/smoke.json" <<'PY' || fail "smoke.json is not what the bot save should have written"
import json, sys
config = json.load(open(sys.argv[1], encoding="utf-8"))
# ASF migrates the file at start-up and drops keys left at their default, such as "Enabled": false
assert config.get("Enabled", False) is False, config
assert config["DemoNotifier"] == {"Enabled": False, "Events": {"FarmingFinished": True}, "Unknown": 1}, config
print("OK: the bot save wrote smoke.json; ASF's bot keys and the unknown key are untouched")
PY
sleep 3
kill -0 "$asf_pid" 2>/dev/null || fail "ASF exited after a bot save"
curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/PluginConfig" > /dev/null || fail "IPC stopped answering after a bot save"
echo "OK: ASF keeps running after a bot save"

stored=$(curl -fsS --max-time 20 -H "Authentication: ${password}" "${ipc}/Api/PluginConfig/Plugins/DemoNotifier/Global") || fail "reading the global value failed"
version=$(printf '%s' "$stored" | sed -n 's/.*"Version":"\([0-9A-F]*\)".*/\1/p')
[ -n "$version" ] || fail "no version in $stored"
case "$stored" in
	*'"ServerUrl":"https://old.example.org"'*) echo "OK: global value read" ;;
	*) fail "unexpected global value: $stored" ;;
esac
case "$stored" in
	*"$token"*) fail "the stored secret was returned: $stored" ;;
	*'"SetSecrets":["AccessToken"]'*) echo "OK: the secret is masked and named as set" ;;
	*) fail "the reply does not name the stored secret as set: $stored" ;;
esac

code=$(save_global "$password" "{\"Value\":{\"ServerUrl\":\"https://new.example.org\",\"DelayMinutes\":0},\"Version\":\"${version}\"}")
[ "$code" = 400 ] || fail "an out-of-range value answered $code, expected 400: $(cat "$work/save.json")"
grep -q '"Path":"DelayMinutes"' "$work/save.json" || fail "the 400 does not name the field: $(cat "$work/save.json")"
echo "OK: 400 names the invalid field"

code=$(save_global "$password" "{\"Version\":\"${version}\"}")
[ "$code" = 400 ] || fail "a body without Value answered $code, expected 400: $(cat "$work/save.json")"
echo "OK: 400 when the body has no Value"

code=$(save_global "$password" '{"Value":{"ServerUrl":"https://new.example.org"},"Version":"0000"}')
[ "$code" = 409 ] || fail "a stale version answered $code, expected 409: $(cat "$work/save.json")"
grep -q -- "$token" "$work/save.json" && fail "a 409 returned the stored secret"
echo "OK: 409 on a stale version"

code=$(save_global "$password" "{\"Value\":{\"ServerUrl\":\"https://new.example.org\",\"DelayMinutes\":10,\"Events\":{\"BotStopped\":false}},\"Version\":\"${version}\"}")
[ "$code" = 200 ] || fail "a valid save answered $code: $(cat "$work/save.json")"
echo "OK: 200 on a valid save"

wait_exit 30
grep -q 'pluginconfig.tmp' <(ls "$work/config") && fail "a temp file was left in config/"
echo "OK: ASF exited to apply the change"

"$python" - "$work/config/ASF.json" "$password" "$token" <<'EOF' || fail "ASF.json is not what the save should have written"
import json, sys
config = json.load(open(sys.argv[1], encoding="utf-8"))
assert config["IPCPassword"] == sys.argv[2] and config["AutoRestart"] is False and config["UpdatePeriod"] == 0, config
demo = config["DemoNotifier"]
assert demo == {"ServerUrl": "https://new.example.org", "DelayMinutes": 10, "Events": {"BotStopped": False}, "AccessToken": sys.argv[3], "Legacy": "kept"}, demo
print("OK: ASF.json has the new value and keeps the omitted secret; ASF's keys and the unknown key are untouched")
EOF

# Second run without IPCPassword: IPC lets localhost in, the host still refuses to write
"$python" - "$work/config/ASF.json" <<'EOF'
import json, sys
config = json.load(open(sys.argv[1], encoding="utf-8"))
del config["IPCPassword"]
json.dump(config, open(sys.argv[1], "w", encoding="utf-8"), indent=2)
EOF

start_asf asf2.out ""
version=$(curl -fsS --max-time 20 "${ipc}/Api/PluginConfig/Plugins/DemoNotifier/Global" | sed -n 's/.*"Version":"\([0-9A-F]*\)".*/\1/p')
code=$(save_global "" "{\"Value\":{\"ServerUrl\":\"https://other.example.org\"},\"Version\":\"${version}\"}")
[ "$code" = 403 ] || fail "a save without IPCPassword answered $code, expected 403: $(cat "$work/save.json")"
grep -q 'IPCPassword' "$work/save.json" || fail "the 403 does not explain itself: $(cat "$work/save.json")"
echo "OK: 403 on a save while IPCPassword is empty"

curl -fsS --max-time 20 -X POST "${ipc}/Api/ASF/Exit" > /dev/null || fail "IPC exit failed"
wait_exit 30

# Third run, still without IPCPassword but with the opt-in on: writes are allowed and the risk is logged and reported
"$python" - "$work/config/ASF.json" <<'EOF'
import json, sys
config = json.load(open(sys.argv[1], encoding="utf-8"))
config["ASFPluginConfig"] = {"AllowWritesWithoutIPCPassword": True}
json.dump(config, open(sys.argv[1], "w", encoding="utf-8"), indent=2)
EOF

start_asf asf3.out ""
grep -q 'AllowWritesWithoutIPCPassword is on and IPCPassword is empty' "$work/asf3.out" || fail "ASF did not log the opt-in warning"
echo "OK: the opt-in warning is logged at start-up"
curl -fsS --max-time 20 "${ipc}/Api/PluginConfig" | grep -q '"WritesWithoutPassword":true' || fail "the host does not report writes without a password"
echo "OK: the host reports writes without a password"
version=$(curl -fsS --max-time 20 "${ipc}/Api/PluginConfig/Plugins/DemoNotifier/Global" | sed -n 's/.*"Version":"\([0-9A-F]*\)".*/\1/p')
code=$(save_global "" "{\"Value\":{\"ServerUrl\":\"https://optin.example.org\"},\"Version\":\"${version}\",\"ClearSecrets\":[\"AccessToken\"]}")
[ "$code" = 200 ] || fail "a save with the opt-in on answered $code: $(cat "$work/save.json")"
echo "OK: 200 on a save with the opt-in on"
wait_exit 30

"$python" - "$work/config/ASF.json" <<'EOF' || fail "ASF.json is not what the opt-in save should have written"
import json, sys
demo = json.load(open(sys.argv[1], encoding="utf-8"))["DemoNotifier"]
assert demo == {"ServerUrl": "https://optin.example.org", "Legacy": "kept"}, demo
print("OK: the cleared secret is gone from ASF.json")
EOF

# ASF's log lines read date|process|LEVEL|logger|message
if grep -E '\|(ERROR|FATAL)\||Exception' "$work"/asf*.out; then
	fail "ASF logged an error or exception"
fi

echo "SMOKE OK"
