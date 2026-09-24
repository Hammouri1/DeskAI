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
  folders here. You can separately allow searching words inside notes, modern Word (`.docx`),
  and Excel (`.xlsx`) files. Nothing read from a file is sent to AI. PDFs, old Office files,
  and what a photo depicts cannot yet be searched inside. **Look in** lets you choose one
  connected folder or search all of them.
- **Automatic tasks** — write rules ("move invoices to Documents"), try them as a practice run,
  and let DeskAI check your folders every so often and tell you when something matches.
- **Desktop Studio** — make your Desktop easier to find your way around. It has four cards:
  **Find groups** sorts what sits on your Desktop into groups and changes nothing on your PC;
  **Put each group in its own folder** and **Clear old stuff** move the things you tick,
  **Add the group's name to each folder's name** renames the folders you tick, and **Put back**
  returns your latest change.
- **My workspace** — starter packs, pinned searches, folder templates, DeskAI's look, and
  your wallpaper.
- **Privacy and AI** — what AI (if any) DeskAI uses and what it may see, your backup, and
  Start fresh.

The top bar on every page has **Find a file…** and a pill that says whether AI is on. The bottom
of the menu always says which folders DeskAI can see.

## The welcome

The first time DeskAI opens, a short welcome appears: what DeskAI is, what it will never do, and a
button for each of your folders. Press **Next** to read on, **Back** to go back, or **Skip** to
close it. It does not come back by itself. A folder button asks the same "Connect your …?"
question Home asks, and connects only if you say yes. To see it again, open **Privacy and AI** and
press **Show the welcome again**.

## Connecting a folder

DeskAI works only inside four of your own folders: **Desktop, Downloads, Documents, and
Pictures**. It cannot be given a drive, a program folder, or anything else on your computer,
even by picking it in a dialog.

On Home (and on My workspace) the **Your folders** card lists the four. Press **Connect** on
one; DeskAI asks first, then reads only the names, sizes, and dates of what is inside and opens
it in Organize. To connect a folder *inside* one of the four, press **Connect a folder** on
Search (or **Choose inside your folders** on Organize) and pick it in the Windows dialog. Connecting
cannot move, rename, delete, or open a file until you allow more, and you can disconnect at any
time on Search, which makes DeskAI forget everything about that folder.

DeskAI looks up to 8 folders deep and at up to 20,000 items in each connected folder. If a
folder is bigger or deeper than that, a note under it on Search says so, and some files may not
show up. When you open Search, DeskAI looks again at any folder it has not checked in the last
10 minutes, so new files show up without pressing **Refresh**. **Refresh** still works any time.

If a choice is refused, read the line below the folder controls. A whole drive, Program Files,
another account's folder, a phone, and a cloud-only place are not valid choices. On a redirected
or OneDrive-backed account, use the Desktop, Downloads, Documents, or Pictures location Windows
shows for that account and make sure it is available locally.

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
an online service you already pay for, with your own key. AI only ever suggests. Before anything
is sent, DeskAI shows you exactly what the service would see and you press Send. What is inside
your files is never sent.

Once it is on, AI can help in two more places. On **Search** and on **Automatic tasks**, type
what you want in your own words ("the slides from my trip last summer", "put my bank statements
somewhere tidy") and press **Let AI read this**. Only those words are sent, nothing about your
files. The AI's reading comes back as plain words in the box, which DeskAI then searches or
fills into the rule boxes, and you can change them. On **Organize**, AI can suggest where
files it cannot place by type belong, or, with **Plan this folder with AI**, suggest folder
names for the whole folder and which file goes where. DeskAI checks every name, only ever makes
folders inside the one you are tidying, and shows you the whole plan before anything moves.

On **Home**, the **Ask DeskAI** box takes a question in your own words: "what's taking space in
Downloads?", "find my slides from last month", "tidy my Desktop". DeskAI asks your permission
before the first question and then remembers it, so it does not interrupt every time; the line
under the box always says which way it stands, and **Ask me each time** brings the question
back. Choosing a different AI service asks again. Only the question is sent. The
AI says what kind of question it is; DeskAI then looks in what it remembers and answers itself,
with one button to open Search or Organize or to connect a folder. It never sends anything about
your files and never moves anything.

## Desktop Studio

Open **Desktop Studio** from the menu. If your Desktop is not connected yet, press **Connect
Desktop**; DeskAI then knows the names of what sits there, reads nothing inside, and moves
nothing.

**Find groups** puts the folders and files on your Desktop into up to 8 groups, plus **Not
sure** for anything that fits nowhere.

- **Use DeskAI's guess** works without AI. DeskAI looks at the kinds of files, for example a
  folder full of `.py` files goes to "Coding", and says the groups are its own simpler guess.
- **Find groups with …** appears when AI is on. First you see the exact list the AI would get:
  each folder's name, the kinds of files inside, and up to 5 file names, and each loose file's
  name. Never what is inside the files, and never where they are. Nothing goes until you press
  **Send**. With online AI, your sharing choices in Privacy and AI must allow file types, file
  names, and folder names; the page tells you if they don't.
- **Rename** a group, **Merge into…** another, or **Move to…** any item. The board is kept when
  you close DeskAI. Something you deleted or renamed on your Desktop drops off the board next
  time, and something new shows up under Not sure.

DeskAI's own program folder is never on the board. Disconnecting the Desktop or Start fresh
forgets the board. Find groups never moves, renames, or opens your files, or changes Windows.

**Clear old stuff** gathers folders and files you haven't changed in 6 months into one **Old
stuff** folder on your Desktop. Under your groups, **Put each group in its own folder** makes a folder on your Desktop for each
group and puts the group's things inside; Not sure stays where it is. Each shows the list first
(on Clear old stuff, press **Show what would move**), untick anything you want to
keep where it is, and press **Move**. The first time, DeskAI asks for your permission to move
things on your Desktop — this is separate from tidying on Organize, and **Stop DeskAI moving
things on my Desktop** takes it back. **Put back** returns your latest change on the Desktop,
even after you close DeskAI. Folders that look like projects or
hold programs start unticked, because moving them can break shortcuts. Anything DeskAI leaves
where it is — a name already taken, something that changed after you looked, something open in
another program — is listed with the reason. Nothing is ever deleted. Your groups stay on the
board: after **Put each group in its own folder**, each group shows the folder that now holds its
things, and **Put back** returns each thing to its group. When one card moves things, the other
cards' lists are cleared; press their button again for an up-to-date list.

If you'd rather keep your folders where they are, **Add the group's name to each folder's name**
(right under your groups) puts each group's name in front of its folders' names, so "Python
stuff" becomes "Coding – Python stuff". You see each folder with its new name first, and only the
ones you tick are renamed when you press **Rename**. Files and Not sure keep their names, a name
already taken is never used, and a folder that already starts with its group's name is left as it
is. It uses the same permission as moving (the dialog asks to "move or rename things on your
Desktop"), and **Put back** gives the folders their old names again.

## Backups, moving computers, and leaving

**Privacy and AI** → **Save a backup file…** writes your rules and saved searches to a file (never
your folders, keys, or locations). **Restore** brings them back after showing you what it would
add; restored rules arrive switched off. **Start fresh** makes DeskAI forget everything it
remembers, without touching a file. Install, update, and removal steps are in `INSTALL.md`.

## If something looks wrong

Every page has small **?** buttons that say what a feature is, what it does, and what it never
does. If DeskAI did something you did not expect, press Undo first, then read
`SECURITY.md` at the repository root for how to report it.
