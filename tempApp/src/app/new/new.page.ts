import { Component, OnInit, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  IonContent,
  IonHeader,
  IonTitle,
  IonToolbar,
  IonButtons,
  IonBackButton,
  IonIcon,
  IonButton,
  IonLabel,
  IonItem,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { heart, logoIonic } from 'ionicons/icons';

@Component({
  selector: 'app-new',
  templateUrl: './new.page.html',
  styleUrls: ['./new.page.scss'],
  standalone: true,
  imports: [
    IonContent,
    IonHeader,
    IonTitle,
    IonToolbar,
    FormsModule,
    IonButtons,
    IonBackButton,
    IonIcon,
    IonButton,
    IonLabel,
    IonItem
  ],
})
export class NewPage implements OnInit {
  
  @ViewChild(IonContent, { static: true }) content!: IonContent;

  item = Array.from({ length: 50 }, (_, i) => `Item ${i + 1}`);
  constructor() {
    addIcons({ heart, logoIonic });
  }

  ngOnInit() {}

  scrollToBottom() { 
    this.content.scrollToBottom(300);
  }
}
