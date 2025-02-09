import { Component, OnInit, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { FormsModule } from '@angular/forms';
import { EcfrApiService, Agency, AgenciesResponse } from '../../services/ecfr-api.service';

@Component({
  selector: 'app-agency-list',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatListModule,
    MatInputModule,
    MatFormFieldModule,
    MatIconModule,
    MatButtonModule,
    FormsModule
  ],
  template: `
    <div class="sidebar">
      <mat-form-field class="search-field" appearance="outline">
        <mat-label>Search agencies</mat-label>
        <input matInput
               [(ngModel)]="searchQuery"
               (ngModelChange)="filterAgencies()"
               placeholder="Enter agency name">
        <button *ngIf="searchQuery"
                matSuffix
                mat-icon-button
                aria-label="Clear"
                (click)="clearSearch()">
          <mat-icon>close</mat-icon>
        </button>
      </mat-form-field>

      <div class="agency-list">
        @if (loading) {
          <div class="loading-message">Loading agencies...</div>
        } @else if (filteredAgencies.length === 0) {
          <div class="no-results">No matching agencies found</div>
        } @else {
          @for (agency of filteredAgencies; track agency.slug) {
            <div class="agency-item" 
                 [class.selected]="agency === selectedAgency"
                 (click)="selectAgency(agency)">
              <div class="agency-info">
                <span class="agency-name">{{ agency.display_name }}</span>
                <div class="agency-titles">
                  @for (ref of agency.cfr_references; track ref.title) {
                    <span class="title-badge">
                      {{ ref.title }}
                    </span>
                  }
                </div>
              </div>
              @if (agency.children && agency.children.length) {
                <span class="child-count">({{ agency.children.length }})</span>
              }
            </div>
            @if (agency === selectedAgency && agency.children && agency.children.length) {
              <div class="child-agencies">
                @for (child of agency.children; track child.slug) {
                  <div class="child-agency-item"
                       [class.selected]="child === selectedAgency"
                       (click)="selectAgency(child); $event.stopPropagation()">
                    <div class="agency-info">
                      <span class="agency-name">{{ child.display_name }}</span>
                      <div class="agency-titles">
                        @for (ref of child.cfr_references; track ref.title) {
                          <span class="title-badge">
                            {{ ref.title }}
                          </span>
                        }
                      </div>
                    </div>
                  </div>
                }
              </div>
            }
          }
        }
      </div>
    </div>
  `,
  styles: [`
    .sidebar {
      width: 300px;
      height: 100vh;
      background: #f5f5f5;
      border-right: 1px solid #ddd;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    .search-field {
      margin: 16px;
    }

    .agency-list {
      flex: 1;
      overflow-y: auto;
      padding: 0 16px 16px;
    }

    .loading-message, .no-results {
      padding: 16px;
      text-align: center;
      color: #666;
    }

    .agency-item, .child-agency-item {
      padding: 12px;
      border-radius: 4px;
      margin: 8px 0;
      cursor: pointer;
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      background: white;
      transition: background-color 0.2s;
    }

    .agency-item:hover, .child-agency-item:hover {
      background: #e0e0e0;
    }

    .agency-item.selected, .child-agency-item.selected {
      background: #e3f2fd;
    }

    .agency-info {
      flex: 1;
    }

    .agency-name {
      display: block;
      margin-bottom: 4px;
      font-weight: 500;
    }

    .agency-titles {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
    }

    .title-badge {
      background: #e0e0e0;
      padding: 2px 6px;
      border-radius: 12px;
      font-size: 0.8em;
    }

    .child-count {
      color: #666;
      font-size: 0.9em;
      margin-left: 8px;
    }

    .child-agencies {
      margin-left: 16px;
    }

    .child-agency-item {
      background: #fafafa;
      margin: 4px 0;
      padding: 8px 12px;
    }
  `]
})
export class AgencyListComponent implements OnInit {
  @Output() agencySelected = new EventEmitter<Agency>();

  agencies: Agency[] = [];
  filteredAgencies: Agency[] = [];
  selectedAgency: Agency | null = null;
  searchQuery = '';
  loading = true;

  constructor(private ecfrService: EcfrApiService) {}

  ngOnInit(): void {
    this.ecfrService.getAgencies().subscribe({
      next: (response: AgenciesResponse) => {
        this.agencies = response.agencies;
        this.filteredAgencies = this.agencies;
        this.loading = false;
      },
      error: (error: Error) => {
        console.error('Error loading agencies:', error);
        this.loading = false;
      }
    });
  }

  filterAgencies(): void {
    if (!this.searchQuery.trim()) {
      this.filteredAgencies = this.agencies;
      return;
    }

    const query = this.searchQuery.toLowerCase();
    this.filteredAgencies = this.agencies.filter(agency => {
      const matchesName = agency.display_name.toLowerCase().includes(query) ||
                         agency.name.toLowerCase().includes(query);
      
      const hasMatchingChildren = agency.children?.some((child: Agency) =>
        child.display_name.toLowerCase().includes(query) ||
        child.name.toLowerCase().includes(query)
      );

      return matchesName || hasMatchingChildren;
    });
  }

  clearSearch(): void {
    this.searchQuery = '';
    this.filterAgencies();
  }

  selectAgency(agency: Agency): void {
    this.selectedAgency = agency;
    this.agencySelected.emit(agency);
  }
}
