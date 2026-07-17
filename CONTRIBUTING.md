# Contributing

AFGC PC Manager is currently pre-release. Bug reports, hardware observations,
documentation corrections, and focused pull requests are welcome.

## Before opening an issue

- Search existing issues first.
- State the controller revision and Windows version when relevant.
- Describe whether vJoy and HidHide were already installed.
- Remove Bluetooth addresses, device instance IDs, usernames, and local paths
  from logs or captures.

## Development workflow

1. Create a branch from `main`.
2. Keep changes focused and preserve existing public behavior unless the change
   is intentional and documented.
3. Add or update hardware-independent tests.
4. Run:

   ```powershell
   dotnet build AFGCPCManager.slnx
   dotnet test AFGCPCManager.slnx --no-build
   ```

5. Explain hardware testing separately from automated testing in the pull
   request. Never claim hardware validation that was not performed.

Do not commit captures, private signing keys, downloaded installers, build
output, or cloned upstream repositories. Do not weaken package verification,
HidHide recovery, or dependency ownership rules merely to make a test pass.

By contributing, you agree that your contribution is licensed under the MIT
License.
