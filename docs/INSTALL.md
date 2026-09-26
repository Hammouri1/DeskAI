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

1. Click **[Download DeskAI 1.3.0 for Windows](https://github.com/Hammouri1/DeskAI/releases/download/v1.3.0/DeskAI-1.3.0-win-x64.zip)**.
   If it does not start, open the [latest release](https://github.com/Hammouri1/DeskAI/releases/latest),
   expand **Assets**, and click `DeskAI-1.3.0-win-x64.zip`.
3. Wait for the download to finish.
4. Open Downloads, right-click the downloaded zip, and choose **Extract All**.
5. Leave the suggested destination selected and press **Extract**.
6. Open the extracted `DeskAI-1.3.0-win-x64` folder.
7. Double-click `DeskAI.App.exe`. Look for the mint DeskAI logo.

Keep the other files beside `DeskAI.App.exe`; they are parts of the application. Do not try to
run the executable directly from inside the zip.

The first time, Windows may show a **"Windows protected your PC"** notice, because the app is
not yet signed with a paid certificate. Choose **More info**, then **Run anyway**.

The zip on this repository's Releases page is the only place DeskAI is published. Do not run a
copy from anywhere else unless you built it yourself from this source.

## Connect your first folder

1. Open **Home** and find **Your folders**.
2. Press **Connect** beside Desktop, Downloads, Documents, or Pictures.
3. Read the question and press **Connect my ...**.

DeskAI deliberately accepts only those four personal folders and folders inside them. If you use
the picker on Search or Organize, do not choose **This PC**, a whole drive, Program Files, a phone,
or a cloud location that has no local path. If Windows has moved a personal folder into OneDrive,
DeskAI uses the location Windows reports; the folder must be available on this computer.

If all four choices are missing or refused, confirm that they open normally in File Explorer,
then close and reopen DeskAI. The message under the folder controls explains whether Windows did
not report a usable location, the place is outside the four personal folders, or it crosses a
link DeskAI cannot follow safely. Include that exact message in a GitHub issue if the problem
continues.

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
