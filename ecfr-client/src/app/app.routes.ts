import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'app/agencies',
    pathMatch: 'full'
  },
  {
    path: 'app/agencies',
    loadComponent: () => import('./components/agency-list/agency-list.component').then(m => m.AgencyListComponent)
  },
  {
    path: 'app/agencies/:slug',
    loadComponent: () => import('./components/agency-detail/agency-detail.component').then(m => m.AgencyDetailComponent)
  },
  {
    path: '**',
    redirectTo: 'app/agencies'
  }
];
