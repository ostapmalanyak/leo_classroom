import { Component, computed, inject, input, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatIcon } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Instant } from '@js-joda/core';
import { AssignedAssignment, AssignmentService } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

interface Section {
  label: string;
  items: AssignedAssignment[];
}

@Component({
  selector: 'app-course-assignments',
  imports: [DatePipe, RouterLink, MatButton, MatCard, MatCardContent, MatCardTitle, MatIcon, MatListModule,
    MatProgressBar],
  templateUrl: './course-assignments.html',
  styleUrl: './course-assignments.scss'
})
export class CourseAssignments implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);

  /** Bound from the route parameter (withComponentInputBinding). */
  public readonly courseId = input.required<string>();

  private readonly all: WritableSignal<AssignedAssignment[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  protected readonly title: Signal<string> = computed(() => this.all()[0]?.courseTitle ?? 'Class');

  protected readonly sections: Signal<Section[]> = computed(() => {
    const now = Instant.now();
    const mine = this.all().filter(a => a.courseId === Number(this.courseId()));
    const byDeadline = (l: AssignedAssignment, r: AssignedAssignment): number =>
      (l.deadline?.toEpochMilli() ?? Infinity) - (r.deadline?.toEpochMilli() ?? Infinity);

    const isPast = (a: AssignedAssignment): boolean => a.deadline !== null && a.deadline.isBefore(now);

    return [
      { label: 'Current', items: mine.filter(a => !isPast(a)).sort(byDeadline) },
      { label: 'Past', items: mine.filter(isPast).sort((l, r) => byDeadline(r, l)) }
    ];
  });

  protected toDate(deadline: Instant): Date {
    return new Date(deadline.toEpochMilli());
  }

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.listMine();
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load your assignments');

      return;
    }
    this.all.set([...result.accepted, ...result.unaccepted]);
  }
}
