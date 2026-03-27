# Large File Commit Checklist (One-Time Setup)

Use this once per repository to avoid GitHub push failures for files larger than 100 MB.

## One-time setup

1. Install Git LFS:
   - Windows: `winget install GitHub.GitLFS`
2. Initialize LFS in your Git client:
   - `git lfs install`
3. Track large file patterns used in this repo:
   - `git lfs track "*.ifc"`
   - `git lfs track "JSON_Edit/*.json"`
4. Commit tracking rules:
   - `git add .gitattributes`
   - `git commit -m "Configure Git LFS tracking"`

## Before every large commit

1. Stage your work:
   - `git add -A`
2. Confirm large files are LFS-tracked:
   - `git lfs ls-files`
3. Commit and push:
   - `git commit -m "Your message"`
   - `git push`

## If push fails with 100 MB error

1. Move the file to LFS and recommit:
   - `git lfs track "path/to/large-file"`
   - `git rm --cached "path/to/large-file"`
   - `git add .gitattributes "path/to/large-file"`
   - `git commit --amend --no-edit`
2. Push updated history safely:
   - `git push --force-with-lease`
