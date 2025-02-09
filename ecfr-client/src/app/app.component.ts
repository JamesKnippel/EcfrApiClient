import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { AgencyListComponent } from './components/agency-list/agency-list.component';
import { AgencyDetailComponent } from './components/agency-detail/agency-detail.component';
import { Agency } from './services/ecfr-api.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    MatToolbarModule,
    MatIconModule,
    MatButtonModule,
    AgencyListComponent,
    AgencyDetailComponent
  ],
  template: `
    <div class="app-container">
      <mat-toolbar color="primary">
        <button mat-icon-button (click)="toggleSidebar()">
          <mat-icon>menu</mat-icon>
        </button>
        <span>ECFR Analytics</span>
        <span class="agency-title" *ngIf="selectedAgency">- {{ selectedAgency.display_name }}</span>
      </mat-toolbar>

      <div class="content-container">
        <app-agency-list 
          [class.sidebar-hidden]="sidebarHidden"
          (agencySelected)="onAgencySelected($event)" 
        />
        <main class="main-content">
          <ng-container *ngIf="selectedAgency">
            <app-agency-detail [agency]="selectedAgency" />
          </ng-container>
          <ng-container *ngIf="!selectedAgency">
            <div class="welcome-message">
              <h1>Welcome to ECFR Analytics</h1>
              <p>Select an agency from the list to view its analytics.</p>
            </div>
          </ng-container>
        </main>
      </div>
    </div>
  `,
  styles: [`
    .app-container {
      display: flex;
      flex-direction: column;
      height: 100vh;
      overflow: hidden;
    }

    mat-toolbar {
      position: relative;
      z-index: 2;
    }

    .agency-title {
      margin-left: 12px;
      font-size: 1rem;
      opacity: 0.9;
    }

    .content-container {
      flex: 1;
      display: flex;
      overflow: hidden;
    }

    app-agency-list {
      width: 300px;
      transition: transform 0.3s ease;
    }

    app-agency-list.sidebar-hidden {
      transform: translateX(-100%);
    }

    .main-content {
      flex: 1;
      overflow-y: auto;
      background: #fafafa;
    }

    .welcome-message {
      padding: 40px;
      text-align: center;
      color: #666;
    }

    .welcome-message h1 {
      margin-bottom: 16px;
      color: #333;
    }

    @media (max-width: 768px) {
      app-agency-list {
        position: absolute;
        top: 64px;
        bottom: 0;
        left: 0;
        background: white;
        z-index: 1;
        box-shadow: 2px 0 5px rgba(0,0,0,0.1);
      }
    }
  `]
})
export class AppComponent {
  selectedAgency: Agency | null = null;
  sidebarHidden = false;

  onAgencySelected(agency: Agency): void {
    this.selectedAgency = agency;
    if (window.innerWidth <= 768) {
      this.sidebarHidden = true;
    }
  }

  toggleSidebar(): void {
    this.sidebarHidden = !this.sidebarHidden;
  }
}
