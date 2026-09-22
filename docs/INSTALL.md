# Installing, updating, and removing DeskAI

DeskAI is a plain folder, not an installer. It writes nothing to Program Files, the registry,
or Windows startup, and it never checks the internet for updates.

## What you need

- Windows 11, version 24H2 or later (build 26100), 64-bit.
- Nothing else. The zip includes everything DeskAI runs on. An ordinary laptop is plenty:
  DeskAI initially reads names, sizes, and dates; reading file contents requires separate
  permission. Connecting a folder of a few
  thousand files takes well under a second (`PERFORMANCE.md`). About 200 MB of disk for the app
  folder; its memory in `%LocalAppData%\DeskAI` is a few megabytes.

## Install

No programming tools or commands are required.

1. Open <https://github.com/Hammouri1/DeskAI/releases/latest>.
2. Find **Assets** and click `DeskAI-1.1.1-win-x64.zip`.
3. Wait for the download to finish.
4. Open Downloads, right-click the downloaded zip, and choose **Extract All**.
5. Leave the suggested destination selected and press **Extract**.
6. Open the extracted `DeskAI-1.1.1-win-x64` folder.
7. Double-click `DeskAI.App.exe`. Look for the mint DeskAI logo.

Keep the other files beside `DeskAI.App.exe`; they are parts of the application. Do not try to
run the executable directly from inside the zip.

The first time, Windows may show a **"Windows protected your PC"** notice, because the app is
not yet signed with a paid certificate. Choose **More info**, then **Run anyway**.

Once releases exist, the zip on the Releases page will be the only place DeskAI is published, and
you should not run a copy from anywhere else. Until then, run only a zip from someone you trust
or one you built yourself from the source.

## If DeskAI does not open

1. Confirm the computer is running 64-bit Windows 11 24H2 or later: open **Settings → System →
   About**, then look under **Windows specifications**.
2. Confirm the zip was extracted and that all its files are still together.
3. Open Task Manager with **Ctrl+Shift+Esc**. If `DeskAI.App` is listed, select it, choose
   **End task**, then double-click `DeskAI.App.exe` again.
4. Check the hidden icons beside the Windows clock for the DeskAI logo; click it if DeskAI was
   asked to continue checking after its window closed.
5. If Windows Security removed or blocked a file, download the zip again from the official
   GitHub Release. Do not disable antivirus protection.

If it still does not open, create a GitHub issue and include the Windows version and what happened:
<https://github.com/Hammouri1/DeskAI/issues>.

## Where DeskAI keeps its memory

- `%LocalAppData%\DeskAI` holds the database: which folders you connected, what it remembered
  about them, your rules and saved searches, your AI choice, and its history.
- Windows Credential Manager holds any AI key you saved, under entries named `DeskAI/…`.
- Nothing else is written anywhere, apart from a backup file you choose to save.

## Update

Download the new zip and unzip it over the old folder (or into a new one). Your memory lives in
`%LocalAppData%\DeskAI`, so it is still there afterwards. The version you are running is shown
at the bottom of **Privacy and AI**.

## Remove

1. In DeskAI, open **Privacy and AI** and press **Start fresh**. This forgets every connected
   folder, rule, saved search, AI choice, and key. Your files are not touched.
2. Close DeskAI and delete the folder you unzipped it into.

If you skipped step 1: delete `%LocalAppData%\DeskAI`, and remove any `DeskAI/…` entries in
Windows Credential Manager (Control Panel › Credential Manager › Windows Credentials).

## Moving to another computer

Save a backup file on **Privacy and AI** (rules and saved searches only), copy it over, and
restore it there. Folders are connected again through the Windows picker on the new computer,
because a permission is never carried in a file, and keys are entered again for the same reason.
