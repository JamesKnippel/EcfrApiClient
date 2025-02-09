import { Component, OnInit, OnDestroy, Input, ViewChild, ElementRef, SimpleChanges, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBarModule, MatSnackBar } from '@angular/material/snack-bar';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { Chart, ChartConfiguration, ChartDataset } from 'chart.js';
import { EcfrApiService, Agency, AgencyWordCountHistory } from '../../services/ecfr-api.service';

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
    MatCardModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  template: `
    <div class="detail-container" *ngIf="agency">
      <div class="header">
        <h1>{{ agency.display_name }}</h1>
        <h2 *ngIf="agency.short_name">{{ agency.short_name }}</h2>
      </div>
      
      <div class="content">
        <div *ngIf="loading" class="loading-container">
          <mat-spinner></mat-spinner>
          <p>Loading word count history...</p>
        </div>

        <div *ngIf="!loading" class="agency-content">
          <mat-card class="references-card">
            <mat-card-header>
              <mat-card-title>CFR References</mat-card-title>
            </mat-card-header>
            <mat-card-content>
              <mat-list>
                <mat-list-item *ngFor="let ref of agency.cfr_references">
                  Title {{ ref.title }} - Chapter {{ ref.chapter }}
                  <span *ngIf="ref.subtitle">({{ ref.subtitle }})</span>
                </mat-list-item>
              </mat-list>
            </mat-card-content>
          </mat-card>

          <div class="charts-container">
            <div class="chart-wrapper">
              <h3>Total Words by Title</h3>
              <canvas #wordCountChart></canvas>
            </div>
            
            <div class="chart-wrapper">
              <h3>Words Added Over Time</h3>
              <canvas #historyChart></canvas>
            </div>
            
            <div class="chart-wrapper">
              <h3>Words Added Per Day</h3>
              <canvas #rateChart></canvas>
            </div>
          </div>

          <div *ngIf="agency.children && agency.children.length" class="child-agencies-section">
            <mat-card>
              <mat-card-header>
                <mat-card-title>Sub-Agencies ({{ agency.children.length }})</mat-card-title>
              </mat-card-header>
              <mat-card-content>
                <div class="child-agencies-grid">
                  <mat-card *ngFor="let child of agency.children" class="child-agency-card">
                    <mat-card-header>
                      <mat-card-title>{{ child.display_name }}</mat-card-title>
                      <mat-card-subtitle *ngIf="child.short_name">{{ child.short_name }}</mat-card-subtitle>
                    </mat-card-header>
                    <mat-card-content>
                      <div class="child-agency-refs">
                        <strong>CFR References:</strong>
                        <span *ngFor="let ref of child.cfr_references; let last = last">
                          Title {{ ref.title }} Ch. {{ ref.chapter }}{{ !last ? ', ' : '' }}
                        </span>
                      </div>
                    </mat-card-content>
                  </mat-card>
                </div>
              </mat-card-content>
            </mat-card>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .detail-container {
      height: 100%;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    .header {
      background: white;
      padding: 20px 40px;
      box-shadow: 0 2px 4px rgba(0,0,0,0.1);
    }

    .header h1 {
      margin: 0;
      font-size: 24px;
      color: #333;
    }

    .header h2 {
      margin: 8px 0 0;
      font-size: 16px;
      color: #666;
      font-weight: normal;
    }

    .content {
      flex: 1;
      overflow-y: auto;
      padding: 20px 40px;
    }
    
    .loading-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 40px;
    }

    .loading-note {
      color: #666;
      font-size: 0.9em;
      margin-top: 10px;
    }

    .agency-content {
      display: flex;
      flex-direction: column;
      gap: 20px;
      max-width: 1400px;
      margin: 0 auto;
    }
    
    .references-card {
      background: white;
    }
    
    .charts-container {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(400px, 1fr));
      gap: 20px;
    }
    
    .chart-wrapper {
      background: white;
      padding: 20px;
      border-radius: 4px;
      box-shadow: 0 2px 4px rgba(0,0,0,0.1);
    }
    
    canvas {
      width: 100% !important;
      height: 350px !important;
    }

    h3 {
      margin: 0 0 15px 0;
      text-align: center;
      color: #333;
    }

    .child-agencies-section {
      margin-top: 20px;
    }

    .child-agencies-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
      gap: 20px;
      padding: 20px 0;
    }

    .child-agency-card {
      background: #f8f8f8;
    }

    .child-agency-refs {
      margin-top: 10px;
      font-size: 0.9em;
    }

    .child-agency-refs strong {
      margin-right: 8px;
      color: #666;
    }

    @media (max-width: 768px) {
      .charts-container {
        grid-template-columns: 1fr;
      }

      .content {
        padding: 10px;
      }

      .header {
        padding: 15px 20px;
      }
    }
  `]
})
export class AgencyDetailComponent implements OnInit, OnDestroy, OnChanges {
  @Input() agency!: Agency;
  @ViewChild('wordCountChart') wordCountChartRef!: ElementRef;
  @ViewChild('historyChart') historyChartRef!: ElementRef;
  @ViewChild('rateChart') rateChartRef!: ElementRef;
  
  loading = false;
  private destroy$ = new Subject<void>();
  private wordCountChart?: Chart;
  private historyChart?: Chart;
  private rateChart?: Chart;

  constructor(
    private ecfrService: EcfrApiService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    if (this.agency) {
      this.loading = true;
      this.loadAgencyData();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['agency'] && !changes['agency'].firstChange) {
      this.loading = true;
      this.loadAgencyData();
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    
    this.wordCountChart?.destroy();
    this.historyChart?.destroy();
    this.rateChart?.destroy();
  }

  private loadAgencyData(): void {
    const endDate = new Date();
    const startDate = new Date(2017, 0, 1);

    this.ecfrService.getAgencyWordCountHistory(this.agency.slug, startDate, endDate).pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: (history: AgencyWordCountHistory) => {
        this.createWordCountChart(history);
        this.createHistoryChart(history);
        this.createRateChart(history);
        this.loading = false;
      },
      error: (error: Error) => {
        console.error('Error loading agency history:', error);
        this.snackBar.open('Error loading agency history', 'Close', { duration: 5000 });
        this.loading = false;
      }
    });
  }

  private createWordCountChart(history: AgencyWordCountHistory): void {
    const data: ChartDataPoint[] = history.titleHistories.map((title: any) => ({
      title: `Title ${title.title}`,
      wordCount: title.wordCounts[title.wordCounts.length - 1]
    }));

    const config: ChartConfiguration = {
      type: 'bar',
      data: {
        labels: data.map((d: ChartDataPoint) => d.title),
        datasets: [{
          label: 'Word Count',
          data: data.map((d: ChartDataPoint) => d.wordCount),
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
            text: `Total Words: ${data.reduce((sum: number, d: ChartDataPoint) => sum + d.wordCount, 0).toLocaleString()}`
          }
        }
      }
    };

    this.wordCountChart?.destroy();
    const ctx = this.wordCountChartRef.nativeElement.getContext('2d');
    this.wordCountChart = new Chart(ctx, config);
  }

  private createHistoryChart(history: AgencyWordCountHistory): void {
    const datasets: ChartDataset<'line', TimeSeriesDataPoint[]>[] = history.titleHistories.map((title: any) => {
      const data = title.wordCounts.map((count: number, index: number) => ({
        x: new Date(title.dates[index]).getTime(),
        y: count
      }));

      return {
        label: `Title ${title.title}`,
        data,
        borderColor: `hsl(${Math.random() * 360}, 70%, 50%)`,
        tension: 0.4,
        fill: false
      };
    });

    const config: ChartConfiguration = {
      type: 'line',
      data: { datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          x: {
            type: 'time',
            time: {
              unit: 'month'
            }
          },
          y: {
            beginAtZero: true
          }
        }
      }
    };

    this.historyChart?.destroy();
    const ctx = this.historyChartRef.nativeElement.getContext('2d');
    this.historyChart = new Chart(ctx, config);
  }

  private createRateChart(history: AgencyWordCountHistory): void {
    const datasets: ChartDataset<'line', TimeSeriesDataPoint[]>[] = history.titleHistories.map((title: any) => {
      const data = title.wordCounts.map((count: number, index: number) => ({
        x: new Date(title.dates[index]).getTime(),
        y: index > 0 ? count - title.wordCounts[index - 1] : 0
      }));

      return {
        label: `Title ${title.title}`,
        data,
        borderColor: `hsl(${Math.random() * 360}, 70%, 50%)`,
        tension: 0.4,
        fill: false
      };
    });

    const config: ChartConfiguration = {
      type: 'line',
      data: { datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          x: {
            type: 'time',
            time: {
              unit: 'month'
            }
          },
          y: {
            beginAtZero: true
          }
        }
      }
    };

    this.rateChart?.destroy();
    const ctx = this.rateChartRef.nativeElement.getContext('2d');
    this.rateChart = new Chart(ctx, config);
  }
}
