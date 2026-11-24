import { Component, computed, DestroyRef, inject, input, OnDestroy, OnInit, signal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { GitSetup, GitSetupService } from '../../../core/services/git-setup-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

type Phase = 'authorized' | 'configured' | 'fresh' | 'loading';

/** The package install differs per distribution; the git config only differs in where the keychain helper is. */
type Distro = 'fedora' | 'ubuntu' | 'arch';

interface DistroSetup {
  readonly label: string;
  readonly install: readonly string[];
  readonly keychain: string;
}

const DISTROS: Record<Distro, DistroSetup> = {
  fedora: {
    label: 'Fedora',
    install: ['sudo dnf install git-credential-oauth git-credential-libsecret'],
    keychain: '/usr/libexec/git-core/git-credential-libsecret'
  },
  ubuntu: {
    label: 'Ubuntu / Debian',
    install: [
      'sudo apt install git-credential-oauth libsecret-1-0 libsecret-1-dev build-essential',
      'sudo make --directory=/usr/share/doc/git/contrib/credential/libsecret'
    ],
    keychain: '/usr/share/doc/git/contrib/credential/libsecret/git-credential-libsecret'
  },
  arch: {
    label: 'Arch',
    install: ['sudo pacman -S git-credential-oauth libsecret'],
    keychain: '/usr/lib/git-core/git-credential-libsecret'
  }
};

/**
 * Whether this browser runs on Windows. `userAgentData.platform` is the Chromium answer and survives user-agent
 * reduction; `navigator.platform` covers the rest ("Win32" on Windows).
 */
function detectWindows(): boolean {
  if (typeof navigator === 'undefined') {
    return false;
  }

  const uaData = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData;
  const platform = uaData?.platform ?? navigator.platform;

  return /^win/i.test(platform) || /Windows/i.test(navigator.userAgent);
}

/**
 * "First: log in to git once" — the step that makes git remember the Forgejo login.
 *
 * <para>
 * The same component serves students and teachers; only the wording of two lines differs. It owns its own
 * state, so a page that shows it never has to know about polling or the distro choice.
 * </para>
 * <para>
 * Instead of piping a downloaded script into a shell, the step copies the commands, and a disclosure shows
 * exactly what was copied. They are the whole setup, nothing hidden, and a student who would not pipe curl
 * into sh can still read and follow them. Since the page cannot see the user's computer, the step clears when
 * they say they ran it, and only a real git operation turns it into "Git login works".
 * </para>
 * <para>
 * It stays out of the way: a close button folds it into the one line that reopens it, and on Windows it never
 * renders at all, because Git Credential Manager keeps the password after the first prompt.
 * </para>
 */
@Component({
  selector: 'app-git-login-step',
  imports: [MatIcon],
  templateUrl: './git-login-step.html',
  styleUrl: './git-login-step.scss'
})
export class GitLoginStep implements OnInit, OnDestroy {
  /** Students are held back by this step; teachers only see a suggestion. */
  public readonly audience = input<'student' | 'teacher'>('student');

  /** The submissions page shows one muted line first and expands on demand. */
  public readonly collapsible = input(false);

  /** Only students may step past the login and type the password by hand. */
  public readonly allowSkip = input(false);

  private readonly service = inject(GitSetupService);
  private readonly snackbar = inject(SnackbarService);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Windows needs none of this: Git Credential Manager stores the password after the first prompt. The browser
   * is asked once, at construction, so the step never renders for a Windows visitor.
   */
  protected readonly windowsDevice = detectWindows();

  protected readonly setup = signal<GitSetup | null>(null);
  protected readonly skipped = signal(false);
  protected readonly expanded = signal(false);
  protected readonly dismissed = signal(false);
  /** macOS: same helper, installed with Homebrew; the keychain is `osxkeychain` instead of the libsecret path. */
  protected readonly mac = signal(false);
  protected readonly stillAsked = signal(false);
  protected readonly copied = signal(false);
  protected readonly saving = signal(false);
  /** The commands are one click away, not on screen: the step is one button until someone wants to read them. */
  protected readonly showCommands = signal(false);

  /** Fedora first: the school's machines run it, and the first tab is the one nobody has to click. */
  protected readonly distro = signal<Distro>('fedora');
  protected readonly distros = Object.entries(DISTROS).map(([id, entry]) => ({ id: id as Distro, ...entry }));

  private timer: ReturnType<typeof setTimeout> | null = null;
  private static readonly POLL_MS = 3000;

  /**
   * True once the step stops holding the next one back: the user confirmed the commands ran, the backend has
   * confirmed a git operation, or the student chose to type the password by hand for this visit.
   */
  public readonly ready = computed(() => {
    const setup = this.setup();

    return this.windowsDevice || this.skipped() ||
      (setup !== null && (setup.configured || setup.authorized));
  });

  /** True only when the backend confirmed it; skipping deliberately does not claim this. */
  public readonly authorized = computed(() => this.setup()?.authorized === true);

  protected readonly phase = computed<Phase>(() => {
    const setup = this.setup();
    if (setup === null) {
      return 'loading';
    }

    return setup.authorized ? 'authorized' : setup.configured ? 'configured' : 'fresh';
  });

  /** The install line(s) for the chosen distribution: the only part that asks for a password. */
  protected readonly installCommand = computed(() => DISTROS[this.distro()].install.join('\n'));

  /** The five git config lines that point git-credential-oauth at this Forgejo. */
  protected readonly configCommand = computed(() => {
    const setup = this.setup();
    if (setup === null) {
      return '';
    }

    // The host is written out in every line instead of a shell variable: `H=...` is a shell assignment and
    // fish rejects it. A literal works in bash, zsh, fish, sh and Windows Git Bash alike.
    const key = `credential.${setup.host}`;

    return [
      `git config --global ${key}.oauthClientId ${setup.clientId}`,
      `git config --global ${key}.oauthAuthURL /login/oauth/authorize`,
      `git config --global ${key}.oauthTokenURL /login/oauth/access_token`,
      `git config --global --add ${key}.helper ${DISTROS[this.distro()].keychain}`,
      `git config --global --add ${key}.helper oauth`
    ].join('\n');
  });

  /** What the copy button copies: both blocks, so one paste is enough. */
  protected readonly command = computed(() =>
    `${this.installCommand()}\n${this.configCommand()}`);

  protected readonly rejectCommand = computed(() =>
    `printf 'protocol=https\\nhost=${this.setup()?.host ?? ''}\\n' | git credential reject`);

  protected readonly mutedLine = computed(() => this.audience() === 'teacher'
    ? 'Cloning student repos? Log in to git once.'
    : 'Git still asks for your password? Fix it once.');

  public constructor() {
    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  public ngOnInit(): void {
    void this.load();
    document.addEventListener('visibilitychange', this.onVisibilityChange);
  }

  public ngOnDestroy(): void {
    document.removeEventListener('visibilitychange', this.onVisibilityChange);
    this.stopPolling();
  }

  protected expand(): void {
    this.dismissed.set(false);
    this.expanded.set(true);
  }

  /** "Not now": the step folds back into the one line that reopens it, for this visit only. */
  protected dismiss(): void {
    this.dismissed.set(true);
    this.showCommands.set(false);
  }

  protected selectDistro(distro: Distro): void {
    this.distro.set(distro);
  }

  protected toggleCommands(): void {
    this.showCommands.update((open) => !open);
  }

  protected toggleMac(): void {
    this.mac.update((open) => !open);
  }

  protected toggleStillAsked(): void {
    this.stillAsked.update((open) => !open);
  }

  protected skip(): void {
    // For this visit only: nothing is written anywhere, which is the point.
    this.skipped.set(true);
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.command());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.snackbar.show('Could not copy the commands');
    }
  }

  /** The user's word that the paste ran. The next git command is what actually proves it. */
  protected async done(): Promise<void> {
    const setup = this.setup();
    if (setup === null || this.saving()) {
      return;
    }

    this.saving.set(true);

    try {
      if (await this.service.markConfigured(setup.token)) {
        this.setup.set({ ...setup, configured: true });
      } else {
        this.snackbar.show('Could not save that. Reload the page and try again.');
      }
    } finally {
      this.saving.set(false);
    }
  }

  private readonly onVisibilityChange = (): void => {
    if (document.hidden) {
      this.stopPolling();
    } else {
      void this.load();
    }
  };

  private async load(): Promise<void> {
    const setup = await this.service.get();
    this.setup.set(setup);

    // Keep asking until the backend confirms it. A null answer is a transient failure (a restart, a dropped
    // connection), not a reason to leave the page without a step.
    if (setup?.authorized === true) {
      this.stopPolling();
    } else {
      this.schedulePoll();
    }
  }

  private schedulePoll(): void {
    this.stopPolling();
    if (document.hidden) {
      return;
    }
    this.timer = setTimeout(() => void this.poll(), GitLoginStep.POLL_MS);
  }

  private async poll(): Promise<void> {
    this.timer = null;
    const setup = await this.service.get();
    this.setup.set(setup);

    if (setup?.authorized !== true) {
      this.schedulePoll();
    }
  }

  private stopPolling(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
