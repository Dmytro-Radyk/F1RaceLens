$ErrorActionPreference = 'Stop'
$assistantRoot = Join-Path (Split-Path (Split-Path $PSScriptRoot)) '.local/assistant'
New-Item -ItemType Directory -Force -Path $assistantRoot | Out-Null
$files = @(
    @{ Name='cuda.zip'; Url='https://github.com/ggml-org/llama.cpp/releases/download/b11808/llama-b11808-bin-win-cuda-12.4-x64.zip'; Hash='74d3f59ebf8019b7209e45a6bd430f8dd2f43a86873f262e22c1901ea62841b0' },
    @{ Name='cudart.zip'; Url='https://github.com/ggml-org/llama.cpp/releases/download/b11808/cudart-llama-bin-win-cuda-12.4-x64.zip'; Hash='8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6' },
    @{ Name='runtime.zip'; Url='https://github.com/ggml-org/llama.cpp/releases/download/b11808/llama-b11808-bin-win-cpu-x64.zip'; Hash='62645783bca4669dcacc8db8e67cda67cd35b5b0d27156c3c23f6666fbd7b03c' },
    @{ Name='instruct.gguf'; Url='https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/a06e946bb6b655725eafa393f4a9745d460374c9/Qwen3-4B-Instruct-2507-Q4_K_M.gguf'; Hash='3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597' }
)
foreach ($file in $files) {
    $target = Join-Path $assistantRoot $file.Name
    if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.Hash) {
        Write-Host "Downloading $($file.Name) from its official publisher..."
        & curl.exe --fail --location --retry 2 --continue-at - --output $target $file.Url
        if ($LASTEXITCODE -ne 0) { throw 'Download incomplete. Run Setup.ps1 again to resume.' }
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.Hash) { throw 'SHA256 mismatch. Do not run this download.' }
}
Expand-Archive -LiteralPath (Join-Path $assistantRoot 'runtime.zip') -DestinationPath (Join-Path $assistantRoot 'runtime') -Force
Expand-Archive -LiteralPath (Join-Path $assistantRoot 'cuda.zip') -DestinationPath (Join-Path $assistantRoot 'cuda') -Force
Expand-Archive -LiteralPath (Join-Path $assistantRoot 'cudart.zip') -DestinationPath (Join-Path $assistantRoot 'cuda') -Force
Write-Host 'Qwen3-4B and llama.cpp verified. Run scripts/assistant/Start.ps1.'
