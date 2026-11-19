# Building the iPhone version (free, no Mac)

GitHub builds the game on its own computers and gives you a `GrandPoliceAuto.ipa`.
Sideloadly then installs it with your normal (free) Apple ID.

## One-time setup

1. **GitHub account** — https://github.com/signup
2. **GitHub Desktop** (easiest way to upload the project) — https://desktop.github.com
   - File -> Add local repository -> pick `C:\Users\joshy\Grand Police Auto`
   - Commit the changes, then **Publish repository** -> keep **"Keep this code private"** ticked.
   - The project uses Git LFS (big files). GitHub Desktop handles that for you.
3. **Unity licence for GitHub** (lets GitHub's computer open Unity as you):
   - Find your licence file on your PC: `C:\ProgramData\Unity\Unity_lic.ulf`
     (open it in Notepad, select all, copy).
   - On GitHub: your repository -> **Settings -> Secrets and variables -> Actions -> New repository secret**. Add three:
     - `UNITY_LICENSE` = the whole text of `Unity_lic.ulf`
     - `UNITY_EMAIL` = the email you sign in to Unity with
     - `UNITY_PASSWORD` = your Unity password

## Every time you want a new build

1. In GitHub Desktop: commit and **Push** your latest changes.
2. On GitHub: **Actions** tab -> **Build iPhone** -> **Run workflow**.
3. Wait (first build ~40–60 min, later ones faster).
4. Open the finished run -> **Artifacts** -> download **GrandPoliceAuto-iPhone** (a zip containing the .ipa).
5. Unzip, open **Sideloadly**, plug in your iPhone, drag the .ipa in, enter your Apple ID, press **Start**.
6. On the iPhone: **Settings -> General -> VPN & Device Management** -> trust your Apple ID.
   (iOS 16+: also turn on **Settings -> Privacy & Security -> Developer Mode**.)

Free Apple ID apps expire after 7 days — just run Sideloadly again (no rebuild needed).

## Free limits

Private repos get 2,000 free build minutes a month; Mac minutes count 10x, so roughly
6–10 iPhone builds a month for free.
