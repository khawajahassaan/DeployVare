#!/bin/bash
# Patches the Tauri-produced .deb to include a preinst maintainer script
# that kills any running api-server before the new binary is installed.
# This prevents dpkg -i from leaving the old process running in memory.
set -e

DEB="src-tauri/target/release/bundle/deb/DeployVare_1.0.0_amd64.deb"
WORK_DIR="$(mktemp -d)"

echo "Patching $DEB with preinst script..."

# Unpack the .deb
dpkg-deb -R "$DEB" "$WORK_DIR"

# Write the preinst script into the control directory
cat > "$WORK_DIR/DEBIAN/preinst" << 'EOF'
#!/bin/sh
# Kill any running api-server so dpkg can replace the binary on disk.
pkill -x api-server 2>/dev/null || true
exit 0
EOF
chmod 755 "$WORK_DIR/DEBIAN/preinst"

# Repack in-place
dpkg-deb -b "$WORK_DIR" "$DEB"

rm -rf "$WORK_DIR"
echo "Done. $DEB now contains preinst script."
