#!/bin/sh
# @APP_NAME@ @APP_VERSION@ (@ARCH_LABEL@) - self-extracting installer.
# Δημιουργήθηκε από το TerraformingMars.Setup. Το tar.gz είναι κολλημένο στο τέλος αυτού
# του αρχείου, μετά τη γραμμή-δείκτη. POSIX sh: τρέχει σε κάθε διανομή, χωρίς εξαρτήσεις.
set -eu

APP_NAME="@APP_NAME@"
APP_VERSION="@APP_VERSION@"
EXE_NAME="@EXE_NAME@"
PKG="@PKG@"
COMMENT="@COMMENT@"

DESKTOP_ICON=1
ASSUME_YES=0
PREFIX=""

usage() {
    cat <<EOF
$APP_NAME $APP_VERSION installer

  sh $0 [options]

  --prefix DIR        install here instead of the default
                      (root: /opt/$PKG, otherwise \$HOME/.local/share/$PKG)
  --no-desktop-icon   only add the application-menu entry, not a desktop icon
  -y, --yes           don't ask anything
  -h, --help          this text

Run as root for a system-wide install, or as your own user for a private one.
Saved games live in \$XDG_DATA_HOME/TerraformingMars (~/.local/share/TerraformingMars)
and are never touched by install or uninstall.
EOF
}

while [ $# -gt 0 ]; do
    case "$1" in
        --prefix) PREFIX="${2:-}"; shift 2 ;;
        --prefix=*) PREFIX="${1#*=}"; shift ;;
        --no-desktop-icon) DESKTOP_ICON=0; shift ;;
        -y|--yes) ASSUME_YES=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
    esac
done

if [ "$(id -u)" = "0" ]; then
    INSTALL_DIR="${PREFIX:-/opt/$PKG}"
    APPS_DIR="/usr/share/applications"
    ICON_DIR="/usr/share/icons/hicolor/256x256/apps"
    BIN_LINK="/usr/local/bin/$PKG"
else
    DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
    INSTALL_DIR="${PREFIX:-$DATA_HOME/$PKG}"
    APPS_DIR="$DATA_HOME/applications"
    ICON_DIR="$DATA_HOME/icons/hicolor/256x256/apps"
    BIN_LINK="$HOME/.local/bin/$PKG"
fi

echo ""
echo "$APP_NAME $APP_VERSION - installer (@ARCH_LABEL@)"
echo "--------------------------------------------------"
echo "  program : $INSTALL_DIR"
echo "  menu    : $APPS_DIR/$PKG.desktop"
[ "$DESKTOP_ICON" = "1" ] && echo "  desktop : an icon on your desktop"

if [ "$ASSUME_YES" != "1" ]; then
    printf "Continue? [Y/n] "
    read -r answer </dev/tty || answer=""
    case "$answer" in [nN]*) echo "Cancelled."; exit 2 ;; esac
fi

# --- ξεπακετάρισμα --------------------------------------------------------
echo "  unpacking..."
mkdir -p "$INSTALL_DIR"
PAYLOAD_LINE=$(awk '/^__TM_PAYLOAD_BELOW__$/ { print NR + 1; exit 0; }' "$0")
tail -n +"$PAYLOAD_LINE" "$0" | gzip -dc | tar -xf - -C "$INSTALL_DIR"

chmod +x "$INSTALL_DIR/$EXE_NAME"
[ -f "$INSTALL_DIR/uninstall.sh" ] && chmod +x "$INSTALL_DIR/uninstall.sh"

# --- εικονίδιο ------------------------------------------------------------
mkdir -p "$ICON_DIR"
cp -f "$INSTALL_DIR/Icon.png" "$ICON_DIR/$PKG.png" 2>/dev/null || true

# --- καταχώρηση στο μενού εφαρμογών --------------------------------------
mkdir -p "$APPS_DIR"
DESKTOP_FILE="$APPS_DIR/$PKG.desktop"
cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=$APP_NAME
Comment=$COMMENT
Exec="$INSTALL_DIR/$EXE_NAME"
Path=$INSTALL_DIR
Icon=$ICON_DIR/$PKG.png
Terminal=false
Categories=Game;Simulation;StrategyGame;
Keywords=mars;colony;terraforming;simulation;
StartupNotify=true
StartupWMClass=$EXE_NAME
EOF
chmod 644 "$DESKTOP_FILE"

# --- εικονίδιο στην επιφάνεια εργασίας -----------------------------------
if [ "$DESKTOP_ICON" = "1" ] && [ "$(id -u)" != "0" ]; then
    DESKTOP_DIR="$HOME/Desktop"
    if [ -f "$HOME/.config/user-dirs.dirs" ]; then
        # σεβασμός σε τοπικοποιημένο όνομα φακέλου (π.χ. "Επιφάνεια εργασίας")
        . "$HOME/.config/user-dirs.dirs" 2>/dev/null || true
        [ -n "${XDG_DESKTOP_DIR:-}" ] && DESKTOP_DIR="$XDG_DESKTOP_DIR"
    fi
    if [ -d "$DESKTOP_DIR" ]; then
        cp -f "$DESKTOP_FILE" "$DESKTOP_DIR/$PKG.desktop"
        chmod +x "$DESKTOP_DIR/$PKG.desktop"
        # GNOME/Nautilus: χωρίς αυτό το εικονίδιο εμφανίζεται ως «μη έμπιστο»
        gio set "$DESKTOP_DIR/$PKG.desktop" metadata::trusted true 2>/dev/null || true
    else
        echo "  (no desktop folder found - skipped the desktop icon)"
    fi
fi

# --- εντολή από τερματικό -------------------------------------------------
mkdir -p "$(dirname "$BIN_LINK")" 2>/dev/null || true
ln -sf "$INSTALL_DIR/$EXE_NAME" "$BIN_LINK" 2>/dev/null || true

# --- απεγκατάσταση --------------------------------------------------------
cat > "$INSTALL_DIR/uninstall.sh" <<EOF
#!/bin/sh
# Απεγκατάσταση του $APP_NAME. Τα saves ΔΕΝ διαγράφονται.
set -eu
echo "Removing $APP_NAME from $INSTALL_DIR"
rm -f "$DESKTOP_FILE" "$ICON_DIR/$PKG.png" "$BIN_LINK"
rm -f "\${XDG_DESKTOP_DIR:-\$HOME/Desktop}/$PKG.desktop" 2>/dev/null || true
rm -rf "$INSTALL_DIR"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" 2>/dev/null || true
echo "Done. Saved games were kept."
EOF
chmod +x "$INSTALL_DIR/uninstall.sh"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f "$(dirname "$(dirname "$(dirname "$ICON_DIR")")")" 2>/dev/null || true

echo ""
echo "$APP_NAME is installed."
echo "  run       : $INSTALL_DIR/$EXE_NAME   (or '$PKG' from a terminal)"
echo "  uninstall : $INSTALL_DIR/uninstall.sh"
echo ""
echo "Note: audio needs the system OpenAL - on Debian/Ubuntu: sudo apt install libopenal1"
exit 0

__TM_PAYLOAD_BELOW__
