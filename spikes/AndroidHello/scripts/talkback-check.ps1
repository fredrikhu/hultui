# Plan 20 phase 6: TalkBack on the emulator (docs/design/TALKBACK.md). Turns TalkBack on with its
# speech in logcat, starts the spike, and checks what a TalkBack user does: the tree Android reads,
# a finger exploring the button (TalkBack says it), a double tap (the click, and the announcement it
# makes, spoken from a live region), swipes in reading order, swiping past the list's last visible
# row (TalkBack scrolls it), and a two-finger drag (passed through, the list scrolls).
#   pwsh spikes/AndroidHello/scripts/talkback-check.ps1 [-Serial emulator-5554] [-LeaveOn]
#
# Emulator only, with adb root: talkback-lib.ps1 says why, and why keyboard-check.ps1 runs first.
# talkback-a11y-check.ps1 checks the rest (text by unit, long press, a dialog, a list of 3,000).
param([string] $Serial = 'emulator-5554', [switch] $LeaveOn)
. "$PSScriptRoot\talkback-lib.ps1"

Start-TalkBack
try {
    Start-Spike
    Check 'the bridge is on' ([bool](Adb logcat -d -s HultUI:I | Select-String 'TalkBack bridge on the surface; accessibility on')) ''

    # 1. The tree Android reads: uiautomator reads it as TalkBack does.
    Read-Tree
    $button = Find 'Tap me' 'android.widget.Button'
    $heading = Find 'HultUI on Android' 'android.widget.TextView'
    $field = Find 'alpha bravo charlie' 'android.widget.EditText'
    $lastRow = @(1..20 | ForEach-Object { Find "Row $_" 'android.widget.(?:TextView|Button)' } | Where-Object { $_ }) | Select-Object -Last 1
    Check 'the tree: a button, a heading, a field, rows' ($button -and $heading -and $field -and $lastRow) ''
    if (-not $button) { throw "no button in the dump: $dump" }

    # 2. A finger on the button: TalkBack focuses it and says it.
    Adb logcat -c
    Hold $button.X $button.Y
    $got = Heard
    Check 'explore: the button is said' ([bool]($got -match '^said: Tap me\. Button$')) ($got -join ' | ')

    # 3. A double tap anywhere clicks it; its announcement is spoken from the live region.
    Adb logcat -c
    DoubleTap ($W / 2) ($H * 0.62)
    $got = Heard
    Check 'double tap clicks' ([bool]($got -match '^bridge: Click$') -and [bool]($got -match '^app: click 1$')) ($got -join ' | ')
    Check 'the announcement is spoken' ([bool]($got -match '^said: Counted 1$')) ''

    # 4. Swipes go on in reading order.
    Hold $heading.X $heading.Y
    Adb logcat -c
    SwipeRight; SwipeRight
    $said = @(Heard | Where-Object { $_ -like 'said:*' })
    Check 'swipes read in order' ($said.Count -ge 2 -and $said[0] -eq 'said: tick 0' -and $said[1] -like 'said: This paragraph*') ($said -join ' | ')

    # 5. Past the list's last visible row, TalkBack scrolls the list on.
    Hold ($W / 2) ($lastRow.Top + 4)
    Adb logcat -c
    foreach ($i in 1..3) { SwipeRight }
    $got = Heard
    $rows = @($got | Where-Object { $_ -match '^said: Row (\d+)$' } | ForEach-Object { [int]($_ -replace '\D', '') })
    Check 'swiping past the end scrolls the list' ([bool]($got -match '^bridge: ScrollForward$') -and ($rows | Where-Object { $_ -gt 12 })) ($got -join ' | ')

    # 6. Two fingers drag the list: TalkBack passes the drag through, and the list scrolls.
    Adb logcat -c
    Swipe ($W * 0.3) ($H * 0.85) ($W * 0.3) ($H * 0.6) -fingers 2 -steps 12 -ms 20
    $scrolled = @(Heard | Where-Object { $_ -match '^app: scrolled' })
    Check 'two fingers scroll the list' ($scrolled.Count -gt 0) "$($scrolled.Count) scroll steps"
}
finally {
    if (-not $LeaveOn) { Stop-TalkBack }
}
if ($failed) { throw "$failed check(s) failed" }
Write-Output 'all TalkBack checks passed'
