# Allure report hosting

The GitHub Actions workflow publishes the latest generated Allure report to the Hetzner server after runs on `main`.

- Server: `46.224.218.150`
- Report URL: `http://46.224.218.150:8089`
- Remote directory: `/opt/desktop-automation-allure/site`
- systemd service: `desktop-automation-allure.service`
- Required GitHub Actions secret: `HETZNER_SSH_KEY`

The private SSH key itself must never be committed to this repository.
