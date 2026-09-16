# DeskAI user guide

DeskAI helps you tidy, find, and understand the files on your Windows computer without giving
anything control of it. It looks only where you let it, moves nothing until you say so, never
deletes, and can put things back.

## The one idea

DeskAI suggests. You decide. Its own code checks every move against your permissions before it
happens, and every move is written down so it can be undone.

## The pages

- **Home** — a welcome, four counts (folders, files, sitting unused, possible duplicates), how
  organized your folders look, where your space is going, and the largest files. Nothing here
  changes anything.
- **Organize** — tidy one folder. Pick it, allow tidying, untick what should stay, press Tidy.
  Undo is right there, even after you close DeskAI. This is also where "Tidy while I'm away"
  lives.
- **Search** — find files by name, type, size, or date ("photos from last month"). Connect
  folders here. You can also let DeskAI read inside plain text files in a folder, separately.
- **Automatic tasks** — write rules ("move invoices to Documents"), try them as a practice run,
  and let DeskAI check your folders every so often and tell you when something matches.
- **My workspace** — starter packs, pinned searches, folder templates, DeskAI's look, and
  your wallpaper.
- **Privacy and AI** — what AI (if any) DeskAI uses and what it may see, your backup, and
  Start fresh.

The top bar on every page has **Find a file…** and a pill that says whether AI is on. The bottom
of the menu always says which folders DeskAI can see.

## Connecting a folder

DeskAI works only inside four of your own folders: **Desktop, Downloads, Documents, and
Pictures**. It cannot be given a drive, a program folder, or anything else on your computer,
even by picking it in a dialog.

On Home (and on My workspace) the **Your folders** card lists the four. Press **Connect** on
one; DeskAI asks first, then reads only the names, sizes, and dates of what is inside and opens
it in Organize. To connect a folder *inside* one of the four, press **Connect a folder** on
Search (or **Choose another folder** on Organize) and pick it in the Windows dialog. Connecting
cannot move, rename, delete, or open a file until you allow more, and you can disconnect at any
time on Search, which makes DeskAI forget everything about that folder.

## Tidying a folder

1. On Organize, pick the folder and press **Allow tidying**. Read the dialog: only loose files
   at the top of the folder, only into folders inside it, never deleted.
2. DeskAI groups its suggestions by where each file would go and says why. Untick anything.
3. Press **Tidy N files**. The result says exactly what moved and what stayed, and **Undo** puts
   it back. If DeskAI is closed part-way through, it asks you what to do next time.

## Rules and checks

Write a rule in your own words or fill in the boxes. **Try a practice run** shows what it would
do without moving anything. **Checking for you** lets DeskAI look on a schedule and tell you
when rules match; **Keep checking after I close the window** keeps that going from an icon near
the clock. Checking on its own never moves a file.

## Tidy while I'm away

For a folder you already allow DeskAI to tidy, turn on **Tidy this folder while I'm away** on
Organize. After a dialog, DeskAI moves loose files your switched-on rules match — at most 25
each time it checks — and stops and waits for you if a rule changes, a file is in the way, or a
file can't be moved. When you come back, a **While you were away** card shows what happened and
lets you undo it. Nothing is ever deleted.

## AI, if you want it

AI is off unless you turn it on in Privacy and AI: either a service running on your computer or
an online service you already pay for, with your own key. AI only ever suggests where a file
might belong. Before anything is sent, DeskAI shows you exactly what the service would see and
you press Send. What is inside your files is never sent.

## Backups, moving computers, and leaving

**Privacy and AI** → **Save a backup file…** writes your rules and saved searches to a file (never
your folders, keys, or locations). **Restore** brings them back after showing you what it would
add; restored rules arrive switched off. **Start fresh** makes DeskAI forget everything it
remembers, without touching a file. Install, update, and removal steps are in `INSTALL.md`.

## If something looks wrong

Every page has small **?** buttons that say what a feature is, what it does, and what it never
does. If DeskAI did something you did not expect, press Undo first, then read
`SECURITY.md` at the repository root for how to report it.
