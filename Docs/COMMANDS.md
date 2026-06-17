# <u> Useful commands </u>

## List Files by Line Count (Largest First)

Includes:

* `*.cs`
* `*.vert`
* `*.frag`

Excludes:

* `bin`
* `obj`

```bash
find . \( -name "*.cs" -o -name "*.vert" -o -name "*.frag" \) \
    ! -path "*/bin/*" \
    ! -path "*/obj/*" \
    -type f \
    -exec wc -l {} + | sort -nr
```

## Total Line Count Only

```bash
find . \( -name "*.cs" -o -name "*.vert" -o -name "*.frag" \) \
    ! -path "*/bin/*" \
    ! -path "*/obj/*" \
    -type f \
    -exec wc -l {} + | awk '{sum += $1} END {print sum}'
```
