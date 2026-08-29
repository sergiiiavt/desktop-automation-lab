#!/usr/bin/env bash
set -euo pipefail

web_port="${1:-8089}"
root=/opt/desktop-automation-allure
service=/etc/systemd/system/desktop-automation-allure.service

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this script as root." >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y python3
fi

mkdir -p "$root/releases"
mkdir -p "$root/site"

cat > "$service" <<EOF
[Unit]
Description=Desktop Automation Allure Report
After=network.target

[Service]
Type=simple
ExecStart=/usr/bin/python3 -m http.server ${web_port} --bind 0.0.0.0 --directory ${root}/site
Restart=always
RestartSec=2

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable desktop-automation-allure.service >/dev/null
systemctl restart desktop-automation-allure.service
systemctl is-active --quiet desktop-automation-allure.service

echo "Allure host is ready on port ${web_port}."
