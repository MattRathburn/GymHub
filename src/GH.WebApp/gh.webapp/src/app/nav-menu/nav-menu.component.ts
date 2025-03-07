import { Component } from '@angular/core';
import { AuthenticationService } from '../authentication.service';
import { Observable } from 'rxjs';
import { NgIf, AsyncPipe } from '@angular/common';
import { provideRouter, RouterLink, RouterLinkActive } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-nav-menu',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    AsyncPipe,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatButtonModule
  ],
  templateUrl: './nav-menu.component.html',
  styleUrl: './nav-menu.component.css'
})
export class NavMenuComponent {
  public username$: Observable<string | undefined>;
  public authenticated$: Observable<boolean>;
  public anonymous$: Observable<boolean>;
  public logoutUrl$: Observable<string | undefined>;
  
  isExpanded = false;

  constructor(private auth: AuthenticationService) {
    auth.getSession();
    this.username$ = auth.getUsername();
    this.authenticated$ = auth.getIsAuthenticated();
    this.anonymous$ = auth.getIsAnonymous();
    this.logoutUrl$ = auth.getLogoutUrl();
  }

  collapse() {
    this.isExpanded = false;
  }

  toggle() {
    this.isExpanded = !this.isExpanded;
  }
}
