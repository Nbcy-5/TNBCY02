# tnbcy-updates

Release host for the TNBCY in-game updater. Do not put game code here.

Every push to `Nbcy-5/TNBCY` (main) publishes a release here (TNBCY.apk, content.pck, version.json) through that repository's build workflow. The game reads `releases/latest/download/version.json` from this repository.
