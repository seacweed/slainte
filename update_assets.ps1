$files = Get-ChildItem -Path Assets -Filter *.asset -Recurse
foreach ($file in $files) {
    $content = Get-Content $file.FullName
    if ($content -match 'Assembly-CSharp::EpisodeData') {
        $newContent = $content -replace 'Assembly-CSharp::EpisodeData', 'Assembly-CSharp::EpisodeBoardData'
        $newContent | Set-Content $file.FullName
        Write-Host "Updated: $($file.FullName)"
    }
}
