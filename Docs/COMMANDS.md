# Useful commands

## List Files by Line Count (Largest First)
```bash
find . \( -name "*.cs" -o -name "*.vert" -o -name "*.frag" \) \
    ! -path "*/bin/*" \
    ! -path "*/obj/*" \
    -type f \
    -exec wc -l {} + | sort -nr
```
