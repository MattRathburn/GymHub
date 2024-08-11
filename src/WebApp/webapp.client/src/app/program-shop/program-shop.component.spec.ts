import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ProgramShopComponent } from './program-shop.component';

describe('ProgramShopComponent', () => {
  let component: ProgramShopComponent;
  let fixture: ComponentFixture<ProgramShopComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ProgramShopComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ProgramShopComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
