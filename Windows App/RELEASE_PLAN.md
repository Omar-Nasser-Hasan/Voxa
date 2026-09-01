# Voxa v1.0.0 — release plan

## Decisions

| Item | Decision |
|---|---|
| Product | One bilingual Windows build: English + Arabic, with an in-app language switch and RTL Arabic layout. |
| Storefront | Gumroad digital product. It provides checkout, buyer receipts, and buyer-gated downloads without building DRM or licensing into v1. |
| Price | **US$2.50/month subscription** or **US$30 one-time lifetime access**. |
| Download | `VoxaSetup.exe` only. Upload a fresh, tested installer for each release and name it `VoxaSetup-1.0.0.exe`. |
| Version | **1.0.0**. The current `Voxa.csproj`, `Services/UpdateChecker.cs`, and `installer/Voxa.iss` already match. |
| Installer | Per-user/no-admin install to `%LocalAppData%\Programs\Voxa`, matching the pre-release test checklist. |
| Code signing | **Deferred for v1.0.0.** Ship unsigned, budget for signing after early sales validate demand, and set buyer expectations about the Windows publisher/reputation warning. |

## Gumroad product setup

Create the lifetime option as a **digital product** from the Products dashboard, upload the final installer, set its price to **$30**, and publish after the clean-machine installation checks are complete. Create the monthly option as a subscription at **$2.50/month** with the same buyer-facing description.

> Important: v1 has no in-app account, entitlement, or license check. A buyer who has already downloaded the installer can continue using it after cancelling a subscription. The monthly option should therefore be framed as access to ongoing installer updates and support, not as software access that automatically stops on cancellation. The $30 option is the true unrestricted lifetime-access product.

**Name**

`Voxa — Batch Audio Converter & Cleaner for Windows`

**Short description**

`Convert and clean up dozens of audio files at once — with format conversion, loudness normalization, sample-rate control, silence trimming, metadata, presets, and English/Arabic UI.`

**Description**

Voxa is a fast, offline-friendly Windows app for creators who need to prepare many audio files without a complicated audio editor.

Convert batches to MP3, WAV, M4A, FLAC, OGG, or AAC. Normalize loudness, adjust volume and speed, enhance speech clarity, trim leading and trailing silence, add metadata, and use reusable presets. Your original files are never changed.

Includes English and Arabic with a mirrored right-to-left Arabic interface.

**What is included**

- Windows installer for Voxa v1.0.0
- Ongoing updates while subscribed, or lifetime access with the one-time option
- English and Arabic interface
- No in-app DRM

**System requirements**

- Windows 10 or Windows 11, 64-bit
- Internet connection only on first run if FFmpeg is not bundled with the installer
- No administrator account required

**Buyer note**

Windows may show a publisher/reputation warning for a new independently distributed application. Download only from this official product page and verify the publisher shown by Windows before opening the installer.

## Release checklist

1. Complete every clean-machine and non-admin check in README section 7.
2. Confirm the three version locations still say `1.0.0`.
3. Build `installer\Output\VoxaSetup.exe` from a clean release checkout.
4. Rename the upload artifact to `VoxaSetup-1.0.0.exe` and upload it to Gumroad.
5. Buy a test copy or use Gumroad's preview flow; confirm the receipt download works.
6. Publish the product URL only after the above steps pass.

## Version rule

For every release, update these values together before building:

- `Voxa.csproj`: `AssemblyVersion` and `FileVersion` use four components, e.g. `1.0.1.0`.
- `Services/UpdateChecker.cs`: `AppVersion.Current` uses the matching three-component public version, e.g. `1.0.1`.
- `installer/Voxa.iss`: `MyAppVersion` uses the matching three-component public version, e.g. `1.0.1`.

Tag the GitHub release as `v1.0.0` once `RepoOwner` and `RepoName` have been set in `UpdateChecker.cs`.
