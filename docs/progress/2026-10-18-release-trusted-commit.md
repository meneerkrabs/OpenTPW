## Release builds run only commits on main (October 18, 2026)

- CodeQL flagged `release.yml` (`actions/cache-poisoning/poisonable-step`): the release jobs check out the commit to build and run its scripts (the native library build, the publish), and a dispatch could name any ref.
- The configure job now refuses any commit that is not on main, for every trigger (a dispatch from another branch, a requested commit, a tag on an unmerged commit), before any later job runs code from it. Before, only a requested commit was checked.
- The checkouts of the build jobs no longer keep the token in the git configuration (`persist-credentials: false`); nothing in them pushes with git (the release is created through `gh api` with the job's token).
