# Installing, updating, and removing DeskAI

DeskAI is a plain folder, not an installer. It writes nothing to Program Files, the registry,
or Windows startup, and it never checks the internet for updates.

## What you need

- Windows 11, version 24H2 or later (build 26100), 64-bit.
- Nothing else. The zip includes everything DeskAI runs on. An ordinary laptop is plenty:
  DeskAI reads names, sizes, and dates, not file contents, and connecting a folder of a few
  thousand files takes well under a second (`PERFORMANCE.md`). About 200 MB of disk for the app
  folder; its memory in `%LocalAppData%\DeskAI` is a few megabytes.

## Install

There is no published release yet, so the Releases page is empty. Until there is one, you will
either be sent a zip or build one yourself with the commands in the README.

1. Get `DeskAI-<version>-win-x64.zip` — from the GitHub **Releases** page once one exists, or
   from whoever sent it to you.
2. Unzip it somewhere you keep programs, for example `C:\Apps\DeskAI`.
3. Run `DeskAI.App.exe`.

The first time, Windows may show a **"Windows protected your PC"** notice, because the app is
not yet signed with a paid certificate. Choose **More info**, then **Run anyway**.

Once releases exist, the zip on the Releases page will be the only place DeskAI is published, and
you should not run a copy from anywhere else. Until then, run only a zip from someone you trust
or one you built yourself from the source.

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
