import { Component } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';

interface CliBinary {
  platform: string;
  rid: string;
  file: string;
}

@Component({
  selector: 'app-cli-download',
  imports: [MatCardModule, MatButton, MatIcon],
  templateUrl: './cli-download.html',
  styleUrl: './cli-download.scss'
})
export class CliDownload {
  // The binaries are produced by the per-RID publish step and served from /downloads/cli.
  protected readonly apiVersion = '1';
  protected readonly binaries: readonly CliBinary[] = [
    { platform: 'Windows (x64)', rid: 'win-x64', file: '/downloads/cli/win-x64/leo-cli.exe' },
    { platform: 'Linux (x64)', rid: 'linux-x64', file: '/downloads/cli/linux-x64/leo-cli' },
    { platform: 'macOS (Apple Silicon)', rid: 'osx-arm64', file: '/downloads/cli/osx-arm64/leo-cli' }
  ];
}
