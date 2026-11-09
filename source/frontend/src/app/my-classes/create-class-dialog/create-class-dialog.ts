import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, HostListener, inject, OnDestroy, signal, ViewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  catchError, debounceTime, distinctUntilChanged, map, merge, of, startWith, Subject, Subscription, switchMap, tap,
  timer
} from 'rxjs';
import { ZodError } from 'zod';
import { ClassRoster, ClassService } from '../../../core/services/class-service';

export interface CreateClassDialogData {
  readonly titleId: string;
}

@Component({
  selector: 'app-create-class-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule, ReactiveFormsModule],
  templateUrl: './create-class-dialog.html',
  styleUrl: './create-class-dialog.scss'
})
export class CreateClassDialog implements OnDestroy {
  private readonly classService = inject(ClassService);
  private readonly dialogRef = inject(MatDialogRef<CreateClassDialog, boolean>);
  protected readonly data = inject<CreateClassDialogData>(MAT_DIALOG_DATA);
  private readonly retrySearch = new Subject<void>();
  private readonly subscriptions = new Subscription();
  private avatarObjectUrl: string | null = null;
  @ViewChild('rosterSearch') private rosterSearch?: ElementRef<HTMLDivElement>;

  protected readonly name = new FormControl('', { nonNullable: true });
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly selectedRoster = signal<ClassRoster | null>(null);
  protected readonly rosters = signal<ClassRoster[]>([]);
  protected readonly searchState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly searchError = signal('');
  protected readonly nameError = signal('');
  protected readonly submitting = signal(false);
  protected readonly avatarUrl = signal<string | null>(null);
  protected readonly avatarLoading = signal(false);
  protected readonly activeIndex = signal(-1);
  protected readonly resultsOpen = signal(false);

  public constructor() {
    const searchRequests = merge(
      this.search.valueChanges.pipe(
        startWith(this.search.value),
        tap(query => {
          this.resultsOpen.set(query.trim().length > 0);
          if (query.trim().length === 0) {
            this.rosters.set([]);
            this.searchState.set('ready');
          } else {
            this.rosters.set([]);
            this.searchState.set('loading');
          }
        }),
        debounceTime(300),
        distinctUntilChanged()
      ),
      this.retrySearch.pipe(map(() => this.search.value))
    );
    this.subscriptions.add(searchRequests.pipe(
      switchMap(query => {
        this.searchState.set('loading');
        this.searchError.set('');
        this.activeIndex.set(-1);

        if (query.trim().length === 0) {
          return of({ results: [], error: null as string | null });
        }

        return this.classService.searchRosters(query.trim()).pipe(
          map(results => ({ results, error: null as string | null })),
          catchError(error => {
            if (error instanceof HttpErrorResponse || error instanceof ZodError) {
              console.error('Could not search class rosters.', error);
              const message = 'Could not load rosters. Try again.';

              return of({ results: null, error: message });
            }
            throw error;
          })
        );
      })
    ).subscribe(result => {
      if (result.error !== null) {
        this.searchState.set('error');
        this.searchError.set(result.error);
        this.rosters.set([]);

        return;
      }

      this.searchState.set('ready');
      this.rosters.set(result.results ?? []);
      this.activeIndex.set(-1);
    }));

    this.subscriptions.add(this.name.valueChanges.pipe(
      startWith(this.name.value),
      distinctUntilChanged(),
      tap(() => {
        this.nameError.set('');
        this.avatarUrl.set(null);
        this.releaseAvatarUrl();
        this.avatarLoading.set(true);
      }),
      switchMap(name => timer(300).pipe(
        switchMap(() => this.classService.getAvatar(name.trim() || 'New class')),
        catchError(error => {
          if (error instanceof HttpErrorResponse) {
            console.error('Could not load the course avatar.', error);

            return of(null);
          }
          throw error;
        })
      ))
    ).subscribe(result => {
      this.avatarLoading.set(false);
      if (result !== null) {
        this.avatarObjectUrl = URL.createObjectURL(result);
        this.avatarUrl.set(this.avatarObjectUrl);
      }
    }));
  }

  protected previewName(): string {
    return this.name.value.trim() || 'New class';
  }

  protected nameIsValid(): boolean {
    const value = this.name.value.trim();

    return value.length > 0 && value.length <= 50;
  }

  protected createIsDisabled(): boolean {
    return !this.nameIsValid() || this.selectedRoster() === null || this.submitting();
  }

  protected retry(): void {
    this.retrySearch.next();
  }

  protected selectRoster(roster: ClassRoster): void {
    this.selectedRoster.set(roster);
    this.resultsOpen.set(false);
  }

  protected removeSelectedRoster(): void {
    this.selectedRoster.set(null);
  }

  protected handleSearchKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.resultsOpen()) {
      event.preventDefault();
      event.stopPropagation();
      this.resultsOpen.set(false);

      return;
    }

    if (event.key === 'ArrowDown' && this.rosters().length > 0) {
      event.preventDefault();
      this.resultsOpen.set(true);
      this.activeIndex.update(index => index < this.rosters().length - 1 ? index + 1 : 0);
    } else if (event.key === 'ArrowUp' && this.rosters().length > 0) {
      event.preventDefault();
      this.resultsOpen.set(true);
      this.activeIndex.update(index => index > 0 ? index - 1 : this.rosters().length - 1);
    } else if (event.key === 'Enter') {
      const activeRoster = this.rosters()[this.activeIndex()];
      if (activeRoster !== undefined) {
        event.preventDefault();
        this.selectRoster(activeRoster);
      }
    }
  }

  protected openRosterResults(): void {
    this.resultsOpen.set(this.search.value.trim().length > 0);
  }

  protected activeOptionId(): string | null {
    const activeRoster = this.rosters()[this.activeIndex()];

    return this.resultsOpen() && activeRoster !== undefined ? `roster-option-${activeRoster.id}` : null;
  }

  @HostListener('document:click', ['$event'])
  protected closeRosterResultsOnOutsideClick(event: MouseEvent): void {
    if (this.rosterSearch !== undefined && !this.rosterSearch.nativeElement.contains(event.target as Node)) {
      this.resultsOpen.set(false);
    }
  }

  protected highlight(name: string): { before: string; match: string; after: string } {
    const query = this.search.value.trim();
    const index = query.length === 0 ? -1 : name.toLocaleLowerCase().indexOf(query.toLocaleLowerCase());
    if (index < 0) {
      return { before: name, match: '', after: '' };
    }

    return {
      before: name.slice(0, index),
      match: name.slice(index, index + query.length),
      after: name.slice(index + query.length)
    };
  }

  protected async create(): Promise<void> {
    const selected = this.selectedRoster();
    if (!this.nameIsValid() || selected === null) {
      return;
    }

    this.submitting.set(true);
    try {
      const result = await this.classService.createClass(this.name.value.trim(), selected.id);
      if (result.kind === 'error') {
        this.nameError.set(result.message);

        return;
      }

      this.dialogRef.close(true);
    } finally {
      this.submitting.set(false);
    }
  }

  public ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.releaseAvatarUrl();
  }

  private releaseAvatarUrl(): void {
    if (this.avatarObjectUrl !== null) {
      URL.revokeObjectURL(this.avatarObjectUrl);
      this.avatarObjectUrl = null;
    }
  }
}
