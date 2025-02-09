import { Component, OnInit, OnDestroy, Input, ViewChild, ElementRef, SimpleChanges, OnChanges, AfterViewInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormGroup, FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBarModule, MatSnackBar } from '@angular/material/snack-bar';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { Chart, ChartConfiguration, ChartDataset, ChartTypeRegistry } from 'chart.js/auto';
import 'chartjs-adapter-date-fns';
import { EcfrApiService, Agency, AgencyWordCountHistory, TitleWordCountHistory } from '../../services/ecfr-api.service';

interface TimeSeriesDataPoint {
  x: number;
  y: number;
}

interface ChartDataPoint {
  title: string;
  wordCount: number;
}

@Component({
  selector: 'app-agency-detail',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule
  ],
  template: `
    <div class="detail-container" *ngIf="agency">
      <div class="header">
        <h1>{{ agency.display_name }}</h1>
        <h2 *ngIf="agency.short_name">{{ agency.short_name }}</h2>
      </div>
      
      <mat-card class="date-range-card">
        <mat-card-header>
          <mat-card-title>Select Date Range</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="dateRange" class="date-range-form">
            <mat-form-field>
              <mat-label>Start Date</mat-label>
              <input matInput [matDatepicker]="startPicker" formControlName="start" [max]="maxDate">
              <mat-datepicker-toggle matSuffix [for]="startPicker"></mat-datepicker-toggle>
              <mat-datepicker #startPicker></mat-datepicker>
            </mat-form-field>

            <mat-form-field>
              <mat-label>End Date</mat-label>
              <input matInput [matDatepicker]="endPicker" formControlName="end" [max]="maxDate">
              <mat-datepicker-toggle matSuffix [for]="endPicker"></mat-datepicker-toggle>
              <mat-datepicker #endPicker></mat-datepicker>
            </mat-form-field>

            <div class="button-container">
              <button mat-raised-button color="primary" 
                      (click)="updateDateRange()" 
                      [disabled]="!dateRange.valid || (!loading && !datesChanged())">
                Update
              </button>
              <button *ngIf="loading" 
                      mat-raised-button 
                      color="warn"
                      (click)="cancelRequest()">
                Cancel
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
      
      <div class="content">
        <div *ngIf="loading" class="loading-container">
          <mat-spinner></mat-spinner>
          <p>Loading word count history...</p>
        </div>

        <mat-card class="references-card" [class.hidden]="loading">
          <mat-card-header>
            <mat-card-title>CFR References</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <mat-list>
              <mat-list-item *ngFor="let ref of agency.cfr_references">
                Title {{ ref.title }} Chapter {{ ref.chapter }}
              </mat-list-item>
            </mat-list>
          </mat-card-content>
        </mat-card>

        <mat-card class="stats-card" [class.hidden]="loading">
          <mat-card-header>
            <mat-card-title>Word Count Statistics</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <div class="stats-grid" *ngIf="wordCountHistory">
              <div class="stat-item">
                <div class="stat-label">Total Words</div>
                <div class="stat-value">{{ getCurrentTotalWordCount() | number }}</div>
              </div>
              <div class="stat-item">
                <div class="stat-label">Words Added</div>
                <div class="stat-value">{{ wordCountHistory.totalWordsAdded | number }}</div>
              </div>
              <div class="stat-item">
                <div class="stat-label">Average Words/Day</div>
                <div class="stat-value">{{ wordCountHistory.averageWordsPerDay | number:'1.0-0' }}</div>
              </div>
              <div class="stat-item">
                <div class="stat-label">Time Period</div>
                <div class="stat-value">{{ getDaysInPeriod() }} days</div>
              </div>
            </div>
          </mat-card-content>
        </mat-card>

        <mat-card class="word-count-card" [class.hidden]="loading">
          <mat-card-header>
            <mat-card-title>Current Word Counts by Title</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <canvas #wordCountChart></canvas>
          </mat-card-content>
        </mat-card>

        <mat-card class="rate-card" [class.hidden]="loading">
          <mat-card-header>
            <mat-card-title>Word Count Changes Over Time</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <canvas #rateChart></canvas>
          </mat-card-content>
        </mat-card>
      </div>
    </div>
  `,
  styles: [`
    .detail-container {
      padding: 20px;
    }
    
    .header {
      margin-bottom: 20px;
    }
    
    .date-range-card {
      margin-bottom: 20px;
    }

    .date-range-form {
      display: flex;
      gap: 16px;
      align-items: baseline;
      flex-wrap: wrap;
    }
    
    .button-container {
      display: flex;
      gap: 8px;
      align-items: center;
    }
    
    .content {
      display: grid;
      gap: 20px;
      grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
    }
    
    .loading-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      min-height: 200px;
    }
    
    .hidden {
      display: none;
    }
    
    mat-card {
      margin-bottom: 20px;
    }
    
    canvas {
      width: 100% !important;
      height: 300px !important;
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 20px;
      padding: 16px;
    }

    .stat-item {
      text-align: center;
      padding: 16px;
      background: #f5f5f5;
      border-radius: 8px;
    }

    .stat-label {
      color: #666;
      font-size: 14px;
      margin-bottom: 8px;
    }

    .stat-value {
      color: #1976d2;
      font-size: 24px;
      font-weight: 500;
    }
  `]
})
export class AgencyDetailComponent implements OnInit, OnDestroy, OnChanges, AfterViewInit {
  @Input() agency?: Agency;
  @ViewChild('wordCountChart') wordCountChartRef!: ElementRef;
  @ViewChild('rateChart') rateChartRef!: ElementRef;

  loading = false;
  wordCountHistory?: AgencyWordCountHistory;
  private wordCountChart?: Chart;
  private rateChart?: Chart;
  private destroy$ = new Subject<void>();
  private loadAbort$ = new Subject<void>();
  private lastLoadedDates: { start: Date | null; end: Date | null } = {
    start: null,
    end: null
  };
  
  dateRange = new FormGroup({
    start: new FormControl<Date | null>(null),
    end: new FormControl<Date | null>(null)
  });
  
  maxDate = new Date();

  constructor(
    private ecfrService: EcfrApiService,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {
    // Load saved dates from local storage
    const savedDates = this.getSavedDates();
    this.dateRange.patchValue({
      start: savedDates.start,
      end: savedDates.end
    });
    this.lastLoadedDates = {
      start: savedDates.start,
      end: savedDates.end
    };
  }

  private getSavedDates(): { start: Date; end: Date } {
    const defaultDates = {
      start: new Date('2017-01-01'),
      end: new Date('2025-02-06')
    };

    try {
      const saved = localStorage.getItem('ecfr-date-range');
      if (saved) {
        const parsed = JSON.parse(saved);
        return {
          start: new Date(parsed.start),
          end: new Date(parsed.end)
        };
      }
    } catch (e) {
      console.error('Error loading saved dates:', e);
    }

    return defaultDates;
  }

  private saveDates(start: Date, end: Date): void {
    try {
      localStorage.setItem('ecfr-date-range', JSON.stringify({
        start: start.toISOString(),
        end: end.toISOString()
      }));
    } catch (e) {
      console.error('Error saving dates:', e);
    }
  }

  ngOnInit(): void {
    if (this.agency) {
      this.loadAgencyData();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['agency'] && !changes['agency'].firstChange) {
      // Cancel any ongoing request before loading new data
      this.loadAbort$.next();
      this.loadAgencyData();
    }
  }

  updateDateRange(): void {
    if (this.dateRange.valid && this.agency) {
      const start = this.dateRange.get('start')?.value;
      const end = this.dateRange.get('end')?.value;
      
      if (start && end) {
        this.saveDates(start, end);
        
        // Cancel any ongoing request before loading new data
        this.loadAbort$.next();
        this.loadAgencyData();
      }
    }
  }

  cancelRequest(): void {
    this.loadAbort$.next();
    this.loading = false;
    this.snackBar.open('Request cancelled', 'Dismiss', {
      duration: 3000,
      horizontalPosition: 'center',
      verticalPosition: 'bottom'
    });
  }

  datesChanged(): boolean {
    const currentStart = this.dateRange.get('start')?.value ?? null;
    const currentEnd = this.dateRange.get('end')?.value ?? null;
    
    return !this.areDatesEqual(currentStart, this.lastLoadedDates.start) ||
           !this.areDatesEqual(currentEnd, this.lastLoadedDates.end);
  }

  private areDatesEqual(date1: Date | null, date2: Date | null): boolean {
    if (!date1 && !date2) return true;
    if (!date1 || !date2) return false;
    return date1.getTime() === date2.getTime();
  }

  private loadAgencyData(): void {
    if (!this.agency?.slug) {
      console.error('No agency slug available');
      return;
    }

    this.loading = true;
    const start = this.dateRange.get('start')?.value ?? null;
    const end = this.dateRange.get('end')?.value ?? null;

    // Convert null dates to undefined to match service parameter types
    const startDate = start ? new Date(start) : undefined;
    const endDate = end ? new Date(end) : undefined;

    // Reset any existing charts
    this.wordCountChart?.destroy();
    this.rateChart?.destroy();

    this.ecfrService.getAgencyWordCountHistory(this.agency.slug, startDate, endDate)
      .pipe(
        takeUntil(this.destroy$),
        takeUntil(this.loadAbort$)
      )
      .subscribe({
        next: (history) => {
          console.log('Received word count history:', history);
          this.wordCountHistory = history;
          
          // Update last loaded dates
          this.lastLoadedDates = {
            start,
            end
          };
          
          // Wait for next tick to ensure view is ready
          setTimeout(() => {
            if (this.wordCountChartRef && this.rateChartRef) {
              console.log('View is ready, creating charts');
              this.createCharts(history);
            } else {
              console.error('Chart elements not found after timeout');
            }
          });
          
          this.loading = false;
          this.cdr.detectChanges();
        },
        error: (error) => {
          console.error('Error loading word count history:', error);
          this.snackBar.open('Error loading word count history', 'Dismiss', {
            duration: 5000,
            horizontalPosition: 'center',
            verticalPosition: 'bottom'
          });
          this.loading = false;
          this.cdr.detectChanges();
        }
      });
  }

  ngAfterViewInit(): void {
    console.log('View initialized');
  }

  ngOnDestroy(): void {
    // Cancel any ongoing requests
    this.loadAbort$.next();
    this.loadAbort$.complete();
    
    this.destroy$.next();
    this.destroy$.complete();
    
    this.wordCountChart?.destroy();
    this.rateChart?.destroy();
  }

  private createCharts(history: AgencyWordCountHistory): void {
    if (!this.wordCountChartRef?.nativeElement || !this.rateChartRef?.nativeElement) {
      console.error('Chart elements not found');
      return;
    }

    console.log('Creating all charts');
    this.createWordCountChart(history);
    this.createRateChart(history);
  }

  private createWordCountChart(history: AgencyWordCountHistory): void {
    const ctx = this.wordCountChartRef.nativeElement.getContext('2d');
    if (!ctx) {
      console.error('Could not get 2D context for word count chart');
      return;
    }

    // Destroy existing chart if it exists
    this.wordCountChart?.destroy();

    const data = history.titleHistories.map(th => ({
      title: `Title ${th.titleNumber}`,
      wordCount: th.wordCounts[th.wordCounts.length - 1]?.wordCount || 0
    }));

    const config: ChartConfiguration = {
      type: 'bar',
      data: {
        labels: data.map(d => d.title),
        datasets: [{
          label: 'Word Count',
          data: data.map(d => d.wordCount),
          backgroundColor: 'rgba(54, 162, 235, 0.5)',
          borderColor: 'rgba(54, 162, 235, 1)',
          borderWidth: 1
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          title: {
            display: true,
            text: 'Current Word Count by Title'
          },
          tooltip: {
            mode: 'index',
            intersect: false
          }
        },
        scales: {
          y: {
            beginAtZero: true,
            title: {
              display: true,
              text: 'Word Count'
            }
          }
        }
      }
    };

    console.log('Creating new word count chart with config:', config);
    this.wordCountChart = new Chart(ctx, config);
  }

  private createRateChart(history: AgencyWordCountHistory): void {
    const ctx = this.rateChartRef.nativeElement.getContext('2d');
    if (!ctx) {
      console.error('Could not get 2D context for rate chart');
      return;
    }

    // Destroy existing chart if it exists
    this.rateChart?.destroy();

    const datasets: ChartDataset<'line', TimeSeriesDataPoint[]>[] = history.titleHistories.map(th => {
      const rates = th.wordCounts.map((wc, i, arr) => {
        if (i === 0) return { x: new Date(wc.date).getTime(), y: 0 };
        const prevCount = arr[i - 1].wordCount;
        const daysDiff = (new Date(wc.date).getTime() - new Date(arr[i - 1].date).getTime()) / (1000 * 60 * 60 * 24);
        const rate = daysDiff > 0 ? (wc.wordCount - prevCount) / daysDiff : 0;
        return { x: new Date(wc.date).getTime(), y: rate };
      });

      return {
        label: `Title ${th.titleNumber} - ${th.titleName}`,
        data: rates,
        fill: false,
        tension: 0.1
      };
    });

    const config: ChartConfiguration = {
      type: 'line',
      data: {
        datasets
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: {
          mode: 'nearest',
          axis: 'x',
          intersect: false
        },
        plugins: {
          title: {
            display: true,
            text: 'Word Count Change Rate'
          },
          tooltip: {
            mode: 'index',
            intersect: false
          }
        },
        scales: {
          x: {
            type: 'time',
            time: {
              unit: 'month'
            },
            title: {
              display: true,
              text: 'Date'
            }
          },
          y: {
            title: {
              display: true,
              text: 'Words Added per Day'
            },
            beginAtZero: true
          }
        }
      }
    };

    console.log('Creating new rate chart with config:', config);
    this.rateChart = new Chart(ctx, config);
  }

  getCurrentTotalWordCount(): number {
    if (!this.wordCountHistory?.titleHistories) return 0;
    
    return this.wordCountHistory.titleHistories.reduce((total: number, title: TitleWordCountHistory) => {
      const latestCount = title.wordCounts[title.wordCounts.length - 1];
      return total + (latestCount?.wordCount || 0);
    }, 0);
  }

  getDaysInPeriod(): number {
    if (!this.wordCountHistory) return 0;
    
    // Parse ISO date strings to Date objects
    const startDate = new Date(this.wordCountHistory.startDate);
    const endDate = new Date(this.wordCountHistory.endDate);
    
    // Handle invalid dates
    if (isNaN(startDate.getTime()) || isNaN(endDate.getTime())) {
      console.error('Invalid date string in word count history', {
        startDate: this.wordCountHistory.startDate,
        endDate: this.wordCountHistory.endDate
      });
      return 0;
    }
    
    return Math.round((endDate.getTime() - startDate.getTime()) / (1000 * 60 * 60 * 24));
  }

  trackByFn(index: number, item: any): number {
    return index;
  }
}
