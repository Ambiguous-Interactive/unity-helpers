# MIT License - Copyright (c) wallstop studios

function ConvertTo-UnitySafeLogText {
    param([AllowNull()][AllowEmptyString()][string]$Text)

    if ($null -eq $Text) {
        return ''
    }

    $Text = $Text -replace '(?i)(\b(?:successfully[ \t]+updated[ \t]+the[ \t]+access[ \t]+token[ \t]+|access[ \t]+token[ \t]*[:=][ \t]*)["'']?)[A-Za-z0-9._~+/=-]+', '$1<redacted:unity-access-token>'
    $Text = $Text -creplace '[A-Z]{2}-[A-Z0-9X]{4}(-[A-Z0-9X]{4}){4}', '[REDACTED-UNITY-SERIAL]'
    return $Text -replace '[\p{L}\p{N}_.%+-]+@[\p{L}\p{N}.-]+\.[a-z]{2,}', '[REDACTED-UNITY-EMAIL]'
}
