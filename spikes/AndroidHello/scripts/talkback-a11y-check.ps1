# Plan 20 phase 6, the rest (docs/design/TALKBACK.md §8): TalkBack on the spike's a11y sample
# (`-e sample a11y`). Checks reading a text area by character (the caret moves with it), a long press
# (TalkBack's double tap held) opening a context menu, Back closing it, and a list of 3,000 items,
# which the tree serves on demand: its items on screen are nodes, a finger finds one, a double tap
# runs it, and swiping past the last one scrolls the list.
#   pwsh spikes/AndroidHello/scripts/talkback-a11y-check.ps1 [-Serial emulator-5554] [-LeaveOn]
#
# Emulator only, with adb root: talkback-lib.ps1 says why, and why keyboard-check.ps1 runs first.
param([string] $Serial = 'emulator-5554', [switch] $LeaveOn)
. "$PSScriptRoot\talkback-lib.ps1"

function FindStart([string] $prefix, [string] $class) {
    $m = [regex]::Match($script:dump, "<node [^>]*text=`"$([regex]::Escape($prefix))[^`"]*`"[^>]*class=`"$class`"[^>]*bounds=`"\[(\d+),(\d+)\]\[(\d+),(\d+)\]`"")
    if (-not $m.Success) { return $null }
    $n = 1..4 | ForEach-Object { [int]$m.Groups[$_].Value }
    @{ X = ($n[0] + $n[2]) / 2; Y = ($n[1] + $n[3]) / 2; Top = $n[1]; Bottom = $n[3] }
}

Start-TalkBack
try {
    Start-Spike @('-e', 'sample', 'a11y')
    Check 'the bridge is on' ([bool](Adb logcat -d -s HultUI:I | Select-String 'TalkBack bridge on the surface; accessibility on')) ''

    # 1. The tree: the text area, the button, and the list's items on screen, of 3,000.
    Read-Tree
    $area = FindStart 'alpha bravo charlie' 'android.widget.EditText'
    $options = Find 'Options' 'android.widget.Button'
    $items = @([regex]::Matches($dump, 'text="Item (\d+)"') | ForEach-Object { [int]$_.Groups[1].Value })
    Check 'the tree: a text area, a button, the items on screen' ($area -and $options -and $items.Count -ge 5 -and $items[0] -eq 0) "items $($items -join ',')"
    if (-not ($area -and $options)) { throw "not in the dump: $dump" }

    # 2. Reading the text area by character while editing it: TalkBack's swipe down, and the caret
    # goes with it. (A field TalkBack is only reading, not editing, it moves through by itself:
    # "Granularity traversal handled by Talkback", and the caret stays.)
    # The double tap puts the caret at the text's end (36), so the moves go back: swipe up.
    Hold $area.X $area.Y
    DoubleTap $area.X $area.Y
    Adb logcat -c
    SwipeUp
    Start-Sleep -Seconds 1
    $got = Heard
    Check 'by character: the last one is said' ([bool]($got -match '^bridge: traversed Character back 35\.\.36$') -and [bool]($got -match '^said: e$')) ($got -join ' | ')
    Adb logcat -c
    SwipeUp
    Start-Sleep -Seconds 1
    $got = Heard
    Check 'by character: the one before, and the caret moved' ([bool]($got -match '^said: r$') -and [bool]($got -match '^app: caret 34 selection 34\.\.34$')) ($got -join ' | ')
    Adb shell input keyevent KEYCODE_BACK   # the keyboard down
    Start-Sleep -Seconds 2

    # 3. A long press on the button (a double tap held, which Android passes on as a press at
    # TalkBack's focus): its context menu opens, and TalkBack's focus goes into it.
    Hold $options.X $options.Y
    Adb logcat -c
    DoubleTapHold $options.X $options.Y
    $got = Heard
    Read-Tree
    Check 'long press opens the context menu' ([bool]($got -match '^app: menu: opening$') -and $dump -match 'text="Rename"') ($got -join ' | ')
    Check 'TalkBack goes into the menu' ([bool]($got -match '^said: Rename')) ''

    # 4. Back closes the menu.
    Adb logcat -c
    Adb shell input keyevent KEYCODE_BACK
    Start-Sleep -Seconds 2
    Read-Tree
    Check 'Back closes the menu' ($dump -notmatch 'text="Rename"') ''

    # 5. The list of 3,000: a finger on an item says it, a double tap runs it.
    $item = Find 'Item 2' 'android.view.View'
    Adb logcat -c
    Hold $item.X $item.Y
    $got = Heard
    Check 'a finger on a served item says it' ([bool]($got -match '^said: Item 2')) ($got -join ' | ')
    Adb logcat -c
    DoubleTap $item.X $item.Y
    $got = Heard
    Check 'a double tap runs it' ([bool]($got -match '^app: activated Item 2$')) ($got -join ' | ')

    # 6. Swiping past the last item on screen scrolls the list, and reads on.
    Read-Tree
    $last = @([regex]::Matches($dump, 'text="Item (\d+)"') | ForEach-Object { [int]$_.Groups[1].Value }) | Select-Object -Last 1
    $lastItem = Find "Item $last" 'android.view.View'
    Hold $lastItem.X $lastItem.Y
    Adb logcat -c
    foreach ($i in 1..3) { SwipeRight }
    $got = Heard
    $said = @($got | Where-Object { $_ -match '^said: Item (\d+)' } | ForEach-Object { [int]($_ -replace '^said: Item (\d+).*', '$1') })
    Check 'swiping past the last scrolls the list' ([bool]($got -match '^bridge: ScrollForward$') -and ($said | Where-Object { $_ -gt $last })) ($got -join ' | ')
}
finally {
    if (-not $LeaveOn) { Stop-TalkBack }
}
if ($failed) { throw "$failed check(s) failed" }
Write-Output 'all TalkBack a11y checks passed'
